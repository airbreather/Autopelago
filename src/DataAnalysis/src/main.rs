use anyhow::Error;
use clap::Parser;
use csv::Reader;
use serde::{Deserialize, Serialize};
use serde_repr::Deserialize_repr;
use status_line::StatusLine;
use std::collections::HashMap;
use std::fmt::{Display, Formatter};
use std::fs::File;
use std::io::{BufReader, Read, Write};
use std::path::PathBuf;
use std::process::ExitCode;
use std::sync::atomic::{AtomicUsize, Ordering};
use std::sync::LazyLock;
use std::thread::spawn;
use std::time::Instant;
use string_interner::backend::StringBackend;
use string_interner::symbol::SymbolU16;
use string_interner::StringInterner;

struct Progress {
    start: Instant,
    movement_runs_done: AtomicUsize,
    location_attempt_runs_done: AtomicUsize,
    total_movements: AtomicUsize,
    total_location_attempts: AtomicUsize,
}

impl Progress {
    fn new(start: Instant) -> Self {
        Self {
            start,
            movement_runs_done: Default::default(),
            location_attempt_runs_done: Default::default(),
            total_movements: Default::default(),
            total_location_attempts: Default::default(),
        }
    }
}

impl Display for Progress {
    fn fmt(&self, f: &mut Formatter<'_>) -> std::fmt::Result {
        write!(
            f,
            "runs_m= {} | moves= {} | runs_l= {} | attempts= {}",
            self.movement_runs_done.fetch_or(0, Ordering::Relaxed),
            self.total_movements.fetch_or(0, Ordering::Relaxed),
            self.location_attempt_runs_done.fetch_or(0, Ordering::Relaxed),
            self.total_location_attempts.fetch_or(0, Ordering::Relaxed),
        )
    }
}

#[derive(Debug, Deserialize_repr, Eq, PartialEq)]
#[repr(u8)]
enum MoveReason {
    GameNotStarted = 0,
    NowhereUsefulToMove = 1,
    ClosestReachableUnchecked = 2,
    Priority = 3,
    PriorityPriority = 4,
    GoMode = 5,
    Startled = 6,
}

#[derive(Debug, Deserialize)]
#[serde(rename_all = "PascalCase")]
struct Movement {
    seed_number: usize,
    iteration_number: usize,
    slot_number: usize,
    step_number: usize,
    from_region: String,
    from_n: usize,
    to_region: String,
    to_n: usize,
    reason: MoveReason,
}

#[derive(Copy, Clone, Debug, Hash, Eq, PartialEq)]
struct LocationKey {
    region_key: SymbolU16,
    n: u8,
}

struct MovementEntry{
    num: u16,
    from: LocationKey,
    to: LocationKey,
    reason: MoveReason,
}

#[derive(Serialize)]
struct ProcessedMovements {
    total_steps: usize,
    total_movements: usize,
    steps_spent_startled: usize,
    movements_while_startled: usize,
    times_moved_to_location: HashMap<String, usize>,
    steps_ended_at_location: HashMap<String, usize>,
}

fn read_movements<R: Read>(movements: R) -> Result<ProcessedMovements, Error> {
    let mut rdr: Reader<R> = Reader::from_reader(movements);
    let mut total_movements = 0usize;
    let mut movements_while_startled = 0usize;
    let mut times_moved_to_location = HashMap::new();
    let mut interner = StringInterner::<StringBackend<SymbolU16>>::new();
    let mut event_map: HashMap<usize, HashMap<usize, HashMap<usize, HashMap<usize, Vec<MovementEntry>>>>> = HashMap::new();
    for result in rdr.deserialize() {
        let record: Movement = result?;
        total_movements += 1;
        let movement_entries_this_step = event_map
            .entry(record.seed_number).or_insert(HashMap::new())
            .entry(record.iteration_number).or_insert(HashMap::new())
            .entry(record.slot_number).or_insert(HashMap::new())
            .entry(record.step_number).or_insert(Vec::new());
        let movement_entry = MovementEntry {
            num: movement_entries_this_step.len() as u16,
            from: LocationKey { region_key: interner.get_or_intern(record.from_region), n: record.from_n as u8 },
            to: LocationKey { region_key: interner.get_or_intern(record.to_region), n: record.to_n as u8 },
            reason: record.reason,
        };
        if movement_entry.reason == MoveReason::Startled {
            movements_while_startled += 1;
        }
        *times_moved_to_location.entry(movement_entry.to).or_insert(0usize) += 1;
        movement_entries_this_step.push(movement_entry);
        STATUS_LINE.movement_runs_done.store((record.seed_number * 1000) + (record.iteration_number * 25) + record.slot_number + 1, Ordering::Relaxed);
        STATUS_LINE.total_movements.fetch_add(1, Ordering::Relaxed);
    }

    let mut total_steps = 0usize;
    let mut steps_spent_startled = 0usize;
    let mut steps_ended_at_location = HashMap::new();
    for iterations in event_map.values() {
        for slots in iterations.values() {
            for steps in slots.values() {
                let max_step = *steps.keys().max().unwrap();
                total_steps += max_step + 1;
                let mut step = 0usize;
                let mut prev_end = LocationKey {
                    region_key: interner.get_or_intern("Menu"),
                    n: 0,
                };
                while step <= max_step {
                    let (curr_end, startled) = match steps.get(&step) {
                        None => (prev_end, false),
                        Some(step_records) => {
                            let last_record = step_records.last().unwrap();
                            (last_record.to, last_record.reason == MoveReason::Startled)
                        }
                    };
                    *steps_ended_at_location.entry(curr_end).or_insert(0) += 1;
                    if startled {
                        steps_spent_startled += 1;
                    }
                    step += 1;
                    prev_end = curr_end;
                }
            }
        }
    }

    Ok(ProcessedMovements {
        total_steps,
        total_movements,
        steps_spent_startled,
        movements_while_startled,
        times_moved_to_location:
            HashMap::from_iter(times_moved_to_location.iter()
                .map(|(k, v)| (format!("{}.{}", interner.resolve(k.region_key).unwrap(), k.n), *v))),
        steps_ended_at_location:
            HashMap::from_iter(steps_ended_at_location.iter()
                .map(|(k, v)| (format!("{}.{}", interner.resolve(k.region_key).unwrap(), k.n), *v))),
    })
}

#[derive(Debug, Deserialize)]
#[serde(rename_all = "PascalCase")]
struct LocationAttempt {
    // SeedNumber,IterationNumber,SlotNumber,StepNumber,Region,N,AbilityCheckDC,Roll,RatCount,Auras
    seed_number: usize,
    iteration_number: usize,
    slot_number: usize,
    step_number: usize,
    region: String,
    n: usize,
    #[serde(rename="AbilityCheckDC")]
    ability_check_dc: usize,
    roll: usize,
    rat_count: usize,
    auras: usize,
}

fn read_location_attempts<R: Read>(location_attempts: R) -> Result<(), Error> {
    let mut rdr: Reader<R> = Reader::from_reader(location_attempts);
    for result in rdr.deserialize() {
        let record: LocationAttempt = result?;
        STATUS_LINE.location_attempt_runs_done.store((record.seed_number * 1000) + (record.iteration_number * 25) + record.slot_number + 1, Ordering::Relaxed);
        STATUS_LINE.total_location_attempts.fetch_add(1, Ordering::Relaxed);
    }

    Ok(())
}

#[derive(Parser)]
struct Args {
    #[arg(index = 1)]
    movements_path: PathBuf,

    #[arg(index = 2)]
    location_attempts_path: PathBuf,

    #[arg(index = 3)]
    results_path: PathBuf,
}

static STATUS_LINE: LazyLock<StatusLine<Progress>> = LazyLock::new(|| StatusLine::new(Progress::new(Instant::now())));

fn main() -> Result<ExitCode, Error> {
    let args = Args::parse();
    let definitions_file = include_bytes!("../../AutopelagoDefinitions.yml");
    let mut results_file = File::create_new(args.results_path)?;

    // run movements
    let movements_handle = spawn(|| {
        let movements = BufReader::new(File::open(args.movements_path).unwrap());
        read_movements(movements).unwrap()
    });

    // run location attempts
    let location_attempts_handle = spawn(|| {
        let location_attempts = BufReader::new(File::open(args.location_attempts_path).unwrap());
        read_location_attempts(location_attempts).unwrap();
    });

    let processed_movements = movements_handle.join().unwrap();
    serde_json::to_writer_pretty(results_file, &processed_movements)?;
    location_attempts_handle.join().unwrap();
    println!("{}", **STATUS_LINE);

    Ok(ExitCode::SUCCESS)
}
