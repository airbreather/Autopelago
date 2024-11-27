use anyhow::Error;
use clap::Parser;
use csv::Reader;
use serde::{Deserialize, Serialize};
use serde_repr::Deserialize_repr;
use status_line::StatusLine;
use std::cmp::{max, min};
use std::collections::HashMap;
use std::fmt::{Display, Formatter};
use std::fs::File;
use std::io::{BufReader, Read};
use std::path::PathBuf;
use std::process::ExitCode;
use std::sync::atomic::{AtomicUsize, Ordering};
use std::sync::LazyLock;
use std::thread::spawn;
use string_interner::backend::StringBackend;
use string_interner::symbol::SymbolU16;
use string_interner::StringInterner;

#[derive(Default)]
struct Progress {
    movement_runs_done: AtomicUsize,
    location_attempt_runs_done: AtomicUsize,
    total_movements: AtomicUsize,
    total_location_attempts: AtomicUsize,
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
    seed_number: u16,
    iteration_number: u16,
    slot_number: u16,
    step_number: u16,
    to_region: String,
    to_n: u8,
    reason: MoveReason,
}

#[derive(Copy, Clone, Debug, Hash, Eq, PartialEq)]
struct LocationKey {
    region_key: SymbolU16,
    n: u8,
}

struct MovementEntry{
    to: LocationKey,
    reason: MoveReason,
}

#[derive(Serialize)]
struct ProcessedMovements {
    total_steps: usize,
    total_movements: usize,
    steps_spent_startled: usize,
    movements_while_startled: usize,
    times_moved_to_region_locations: HashMap<String, Vec<usize>>,
    steps_ended_at_region_locations: HashMap<String, Vec<usize>>,
}

fn read_movements<R: Read>(movements: R) -> Result<ProcessedMovements, Error> {
    let mut rdr: Reader<R> = Reader::from_reader(movements);
    let mut total_movements = 0usize;
    let mut movements_while_startled = 0usize;
    let mut times_moved_to_location = HashMap::new();
    let mut interner = StringInterner::<StringBackend<SymbolU16>>::new();
    let mut event_map: HashMap<u16, HashMap<u16, HashMap<u16, HashMap<u16, Vec<MovementEntry>>>>> = HashMap::new();
    for result in rdr.deserialize() {
        let record: Movement = result?;
        total_movements += 1;
        let movement_entries_this_step = event_map
            .entry(record.seed_number).or_insert(HashMap::new())
            .entry(record.iteration_number).or_insert(HashMap::new())
            .entry(record.slot_number).or_insert(HashMap::new())
            .entry(record.step_number).or_insert(Vec::new());
        let movement_entry = MovementEntry {
            to: LocationKey { region_key: interner.get_or_intern(record.to_region), n: record.to_n },
            reason: record.reason,
        };
        if movement_entry.reason == MoveReason::Startled {
            movements_while_startled += 1;
        }
        *times_moved_to_location.entry(movement_entry.to).or_insert(0usize) += 1;
        movement_entries_this_step.push(movement_entry);
        STATUS_LINE.movement_runs_done.store((record.seed_number as usize * 1000) + (record.iteration_number as usize * 25) + record.slot_number as usize + 1, Ordering::Relaxed);
        STATUS_LINE.total_movements.fetch_add(1, Ordering::Relaxed);
    }

    let mut total_steps = 0usize;
    let mut steps_spent_startled = 0usize;
    let mut steps_ended_at_location = HashMap::new();
    for iterations in event_map.values() {
        for slots in iterations.values() {
            for steps in slots.values() {
                let max_step = *steps.keys().max().unwrap();
                total_steps += max_step as usize + 1;
                let mut step = 0u16;
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

    let mut times_moved_to_region_locations = HashMap::new();
    for (k, v) in times_moved_to_location {
        (*times_moved_to_region_locations.entry(k.region_key).or_insert(Vec::new()))
            .push((k.n, v));
    }

    for v in times_moved_to_region_locations.values_mut() {
        v.sort_by_key(|e| e.0);
    }

    let mut steps_ended_at_region_locations = HashMap::new();
    for (k, v) in steps_ended_at_location {
        (*steps_ended_at_region_locations.entry(k.region_key).or_insert(Vec::new()))
            .push((k.n, v));
    }

    for v in steps_ended_at_region_locations.values_mut() {
        v.sort_by_key(|e| e.0);
    }

    Ok(ProcessedMovements {
        total_steps,
        total_movements,
        steps_spent_startled,
        movements_while_startled,
        times_moved_to_region_locations:
            HashMap::from_iter(times_moved_to_region_locations.iter()
                .map(|(k, v)| (String::from(interner.resolve(*k).unwrap()), v.iter().map(|e| e.1).collect()))),
        steps_ended_at_region_locations:
            HashMap::from_iter(steps_ended_at_region_locations.iter()
                .map(|(k, v)| (String::from(interner.resolve(*k).unwrap()), v.iter().map(|e| e.1).collect()))),
    })
}

#[derive(Debug, Deserialize)]
#[serde(rename_all = "PascalCase")]
struct LocationAttempt {
    seed_number: u16,
    iteration_number: u16,
    slot_number: u16,
    step_number: u16,
    region: String,
    n: u8,
    rat_count: u8,
    auras: u8,
}

#[derive(Default)]
struct LocationAttemptAccumulator {
    step_number: Vec<u16>,
    rat_count: Vec<u16>,
    lucky: usize,
    unlucky: usize,
    stylish: usize,
}

#[derive(Serialize)]
struct Statistics {
    min: u16,
    max: u16,
    mean: f64,
    median: u16,
    mode: u16,
    sd: f64,
}

fn get_mut_at_with_extend<T>(v: &mut Vec<T>, index: usize) -> &mut T where T: Default {
    while index >= v.len() {
        v.push(Default::default());
    }

    v.get_mut(index).unwrap()
}

impl Statistics {
    fn of(vals: &mut Vec<u16>) -> Self {
        vals.sort();
        let median: u16 = {
            if (vals.len() & 1) == 0 {
                if vals.len() == 1 {
                    *vals.get(0).unwrap()
                } else{
                    let a = *vals.get((vals.len() >> 1) + 0).unwrap();
                    let b = *vals.get((vals.len() >> 1) + 1).unwrap();
                    ((a as u32 + b as u32 + 1) >> 1) as u16
                }
            } else {
                *vals.get(vals.len() >> 1).unwrap()
            }
        };

        let mut min_val = u16::MAX;
        let mut max_val = u16::MIN;
        let mut sum_of_vals = 0u64;
        let mut num_of_vals = 0usize;
        let mut counts = Vec::<usize>::new();
        for val in vals.iter() {
            min_val = min(min_val, *val);
            max_val = max(max_val, *val);
            sum_of_vals += *val as u64;
            num_of_vals += 1;
            *get_mut_at_with_extend(&mut counts, *val as usize) += 1;
        }

        let mut mode = 0u16;
        let mut mode_count = 0usize;
        for (i, count) in counts.iter().enumerate() {
            if *count > mode_count {
                mode = i as u16;
                mode_count = *count;
            }
        }

        let mean = sum_of_vals as f64 / num_of_vals as f64;

        let mut variance_numerator = 0f64;
        for n in vals.iter() {
            let dev = *n as f64 - mean;
            variance_numerator += dev * dev;
        }

        let sd = if num_of_vals == 1 {
            -1f64
        } else {
            variance_numerator.sqrt() / (num_of_vals - 1) as f64
        };

        Self {
            min: min_val,
            max: max_val,
            mean,
            median,
            mode,
            sd,
        }
    }
}

#[derive(Serialize)]
struct LocationAttemptSummary {
    step_number: Statistics,
    rat_count: Statistics,
    lucky_proportion: f64,
    unlucky_proportion: f64,
    stylish_proportion: f64,
}

#[derive(Serialize)]
struct ProcessedLocationAttempts {
    total_attempts: usize,
    summarized_location_attempts_by_region: HashMap<String, LocationAttemptSummary>,
    summarized_location_attempts_by_region_location: HashMap<String, Vec<LocationAttemptSummary>>,
}

impl LocationAttemptSummary {
    fn summarize(accumulator: &mut LocationAttemptAccumulator) -> Self {
        Self {
            lucky_proportion: accumulator.lucky as f64 / accumulator.step_number.len() as f64,
            unlucky_proportion: accumulator.unlucky as f64 / accumulator.step_number.len() as f64,
            stylish_proportion: accumulator.stylish as f64 / accumulator.step_number.len() as f64,
            step_number: Statistics::of(&mut accumulator.step_number),
            rat_count: Statistics::of(&mut accumulator.rat_count),
        }
    }
}

fn read_location_attempts<R: Read>(location_attempts: R) -> Result<ProcessedLocationAttempts, Error> {
    let mut rdr: Reader<R> = Reader::from_reader(location_attempts);
    let mut total_attempts = 0usize;
    let mut interner = StringInterner::<StringBackend<SymbolU16>>::new();
    let mut region_accumulators = HashMap::new();
    let mut region_location_accumulators = HashMap::new();
    for result in rdr.deserialize() {
        let record: LocationAttempt = result?;
        total_attempts += 1;
        let region_accumulator = region_accumulators
            .entry(interner.get_or_intern(&record.region)).or_insert(LocationAttemptAccumulator::default());
        region_accumulator.step_number.push(record.step_number);
        region_accumulator.rat_count.push(record.rat_count as u16);
        region_accumulator.lucky += ((record.auras & (1 << 0)) >> 0) as usize;
        region_accumulator.unlucky += ((record.auras & (1 << 1)) >> 1) as usize;
        region_accumulator.stylish += ((record.auras & (1 << 2)) >> 2) as usize;

        let region_location_accumulator =
            get_mut_at_with_extend(
                region_location_accumulators
                    .entry(interner.get_or_intern(&record.region))
                    .or_insert(Vec::<LocationAttemptAccumulator>::new()),
                record.n as usize,
            );
        region_location_accumulator.step_number.push(record.step_number);
        region_location_accumulator.rat_count.push(record.rat_count as u16);
        region_location_accumulator.lucky += ((record.auras & (1 << 0)) >> 0) as usize;
        region_location_accumulator.unlucky += ((record.auras & (1 << 1)) >> 1) as usize;
        region_location_accumulator.stylish += ((record.auras & (1 << 2)) >> 2) as usize;
        STATUS_LINE.location_attempt_runs_done.store((record.seed_number as usize * 1000) + (record.iteration_number as usize * 25) + record.slot_number as usize + 1, Ordering::Relaxed);
        STATUS_LINE.total_location_attempts.fetch_add(1, Ordering::Relaxed);
    }

    Ok(ProcessedLocationAttempts {
        total_attempts,
        summarized_location_attempts_by_region:
            HashMap::from_iter(region_accumulators.iter_mut()
                .map(|(k, v)| (String::from(interner.resolve(*k).unwrap()), LocationAttemptSummary::summarize(v)))),
        summarized_location_attempts_by_region_location: HashMap::from_iter(region_location_accumulators.iter_mut()
            .map(|(k, v)| (
                String::from(interner.resolve(*k).unwrap()),
                v.iter_mut().map(|a| LocationAttemptSummary::summarize(a)).collect()))),
    })
}

#[derive(Serialize)]
struct FinalResult {
    movements: ProcessedMovements,
    location_attempts: ProcessedLocationAttempts,
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

static STATUS_LINE: LazyLock<StatusLine<Progress>> = LazyLock::new(|| StatusLine::new(Progress::default()));

fn main() -> Result<ExitCode, Error> {
    let args = Args::parse();
    let definitions_file = include_bytes!("../../AutopelagoDefinitions.yml");
    let results_file = File::create_new(args.results_path)?;

    // run movements
    let movements_handle = spawn(|| {
        let movements = BufReader::new(File::open(args.movements_path).unwrap());
        read_movements(movements).unwrap()
    });

    // run location attempts
    let location_attempts_handle = spawn(|| {
        let location_attempts = BufReader::new(File::open(args.location_attempts_path).unwrap());
        read_location_attempts(location_attempts).unwrap()
    });

    let result = FinalResult {
        movements: movements_handle.join().unwrap(),
        location_attempts: location_attempts_handle.join().unwrap(),
    };
    serde_json::to_writer_pretty(results_file, &result)?;
    println!("{}", **STATUS_LINE);

    Ok(ExitCode::SUCCESS)
}
