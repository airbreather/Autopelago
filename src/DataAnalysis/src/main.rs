use anyhow::Error;
use bitvec::vec::BitVec;
use clap::Parser;
use csv::Reader;
use serde::{Deserialize, Serialize};
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
use string_interner::backend::StringBackend;
use string_interner::symbol::SymbolU16;
use string_interner::StringInterner;
use zstd::Decoder;

#[derive(Default)]
struct Progress {
    location_attempt_runs_done: AtomicUsize,
    total_location_attempts: AtomicUsize,
}

impl Display for Progress {
    fn fmt(&self, f: &mut Formatter<'_>) -> std::fmt::Result {
        write!(
            f,
            "runs= {} | attempts= {}",
            self.location_attempt_runs_done.fetch_or(0, Ordering::Relaxed),
            self.total_location_attempts.fetch_or(0, Ordering::Relaxed),
        )
    }
}

#[derive(Debug, Deserialize)]
#[serde(rename_all = "PascalCase")]
struct LocationAttempt {
    // SeedNumber,IterationNumber,SlotNumber,StepNumber,Region,N,AbilityCheckDC,RatCount,HasLucky,HasUnlucky,HasStylish,Roll,Success
    seed_number: u16,
    iteration_number: u16,
    slot_number: u16,
    step_number: u16,
    region: String,
    n: u8,
    rat_count: u8,
    mercy_modifier: u8,
    has_lucky: u8,
    has_unlucky: u8,
    has_stylish: u8,
    success: u8,
}

#[derive(Default)]
struct LocationAttemptAccumulator {
    step_number: Vec<u16>,
    rat_count: Vec<u16>,
    mercy_modifier: Vec<u16>,
    attempted_during: BitVec,
    lucky: usize,
    unlucky: usize,
    stylish: usize,
    success: usize,
}

#[derive(Serialize)]
struct Statistics {
    num: u64,
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

fn set_at_with_extend(v: &mut BitVec, index: usize) {
    while index >= v.len() {
        v.push(false)
    }

    v.set(index, true);
}

impl Statistics {
    fn of(vals: &mut Vec<u16>) -> Self {
        vals.sort();
        let num_vals = vals.len() as u64;
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
        let mut counts = Vec::<usize>::new();
        for val in vals.iter() {
            min_val = min(min_val, *val);
            max_val = max(max_val, *val);
            sum_of_vals += *val as u64;
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

        let mean = sum_of_vals as f64 / num_vals as f64;

        let mut variance_numerator = 0f64;
        for n in vals.iter() {
            let dev = *n as f64 - mean;
            variance_numerator += dev * dev;
        }

        let sd = if num_vals == 1 {
            -1f64
        } else {
            variance_numerator.sqrt() / (num_vals - 1) as f64
        };

        Self {
            num: num_vals,
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
    mercy_modifier: Statistics,
    runs_attempted: u64,
    success_proportion: f64,
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
            success_proportion: accumulator.success as f64 / accumulator.step_number.len() as f64,
            lucky_proportion: accumulator.lucky as f64 / accumulator.step_number.len() as f64,
            unlucky_proportion: accumulator.unlucky as f64 / accumulator.step_number.len() as f64,
            stylish_proportion: accumulator.stylish as f64 / accumulator.step_number.len() as f64,
            runs_attempted: accumulator.attempted_during.count_ones() as u64,
            step_number: Statistics::of(&mut accumulator.step_number),
            rat_count: Statistics::of(&mut accumulator.rat_count),
            mercy_modifier: Statistics::of(&mut accumulator.mercy_modifier),
        }
    }
}

fn read_location_attempts<R: Read>(location_attempts: R) -> Result<ProcessedLocationAttempts, Error> {
    let mut prev_seed_number = u16::MAX;
    let mut prev_iteration_number = u16::MAX;
    let mut prev_slot_number = u16::MAX;
    let mut run_index = usize::MAX;
    let mut rdr: Reader<R> = Reader::from_reader(location_attempts);
    let mut total_attempts = 0usize;
    let mut interner = StringInterner::<StringBackend<SymbolU16>>::new();
    let mut region_accumulators = HashMap::new();
    let mut region_location_accumulators = HashMap::new();
    for result in rdr.deserialize() {
        let record: LocationAttempt = result?;
        total_attempts += 1;
        if (prev_seed_number, prev_iteration_number, prev_slot_number) != (record.seed_number, record.iteration_number, record.slot_number) {
            run_index += 1;
            (prev_seed_number, prev_iteration_number, prev_slot_number) = (record.seed_number, record.iteration_number, record.slot_number);
        }
        let region_accumulator = region_accumulators
            .entry(interner.get_or_intern(&record.region)).or_insert(LocationAttemptAccumulator::default());
        region_accumulator.step_number.push(record.step_number);
        region_accumulator.rat_count.push(record.rat_count as u16);
        region_accumulator.mercy_modifier.push(record.mercy_modifier as u16);
        set_at_with_extend(&mut region_accumulator.attempted_during, run_index);
        region_accumulator.success += record.success as usize;
        region_accumulator.lucky += record.has_lucky as usize;
        region_accumulator.unlucky += record.has_unlucky as usize;
        region_accumulator.stylish += record.has_stylish as usize;

        let region_location_accumulator =
            get_mut_at_with_extend(
                region_location_accumulators
                    .entry(interner.get_or_intern(&record.region))
                    .or_insert(Vec::<LocationAttemptAccumulator>::new()),
                record.n as usize,
            );
        region_location_accumulator.step_number.push(record.step_number);
        region_location_accumulator.rat_count.push(record.rat_count as u16);
        region_location_accumulator.mercy_modifier.push(record.mercy_modifier as u16);
        set_at_with_extend(&mut region_location_accumulator.attempted_during, run_index);
        region_location_accumulator.success += record.success as usize;
        region_location_accumulator.lucky += record.has_lucky as usize;
        region_location_accumulator.unlucky += record.has_unlucky as usize;
        region_location_accumulator.stylish += record.has_stylish as usize;
        STATUS_LINE.location_attempt_runs_done.store(run_index, Ordering::Relaxed);
        STATUS_LINE.total_location_attempts.fetch_add(1, Ordering::Relaxed);
    }

    STATUS_LINE.location_attempt_runs_done.store(run_index + 1, Ordering::Relaxed);

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

#[derive(Parser)]
struct Args {
    #[arg(index = 1)]
    location_attempts_path: PathBuf,

    #[arg(index = 2)]
    results_path: PathBuf,

    #[arg(long)]
    force: bool,
}

static STATUS_LINE: LazyLock<StatusLine<Progress>> = LazyLock::new(|| StatusLine::new(Progress::default()));

fn main() -> Result<ExitCode, Error> {
    let args = Args::parse();
    let definitions_file = include_bytes!("../../AutopelagoDefinitions.yml");
    let results_file = if args.force {
        File::create(args.results_path)
    } else {
        File::create_new(args.results_path)
    }?;

    // run location attempts
    let location_attempts = BufReader::new(Decoder::new(File::open(args.location_attempts_path)?)?);
    let result = read_location_attempts(location_attempts)?;
    serde_json::to_writer_pretty(results_file, &result)?;
    println!("{}", **STATUS_LINE);

    Ok(ExitCode::SUCCESS)
}
