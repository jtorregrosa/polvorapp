#!/usr/bin/env node
// Generates the seed's committed images (realistic-seed-data, design D5) with Z-Image Turbo
// (Tongyi-MAI, Apache-2.0) through Runpod's public endpoint:
// - faces: ID photos of people who do not exist, matched by the registry seeder on gender and age;
// - logos: invented heraldic emblems of the seeded comparsas, without text.
// Raw outputs are cached in the git-ignored seed-assets/raw/<kind>/, so a rerun only pays for the
// gaps. They are then optimised into backend/synthetic-data/<kind>/ with a manifest, which is
// committed. The same inputs always give the same prompts, seeds and files.
//
// Usage: node scripts/generate-seed-images.mjs --kind faces|logos [--count 400] [--concurrency 4] [--dry-run]
// RUNPOD_API_KEY is read from the environment, only when an image is missing from the cache.
// Needs `npm ci` in frontend/: sharp comes from there (through @vite-pwa/assets-generator).
// Cost: USD 0.005 per image. A cached raw image is reused only while its prompt, seed and size are
// unchanged (a .json sidecar records them), so editing a prompt regenerates that image.
// Exit codes: 0 done, 1 some images failed, 2 usage error.
import { createHash } from 'node:crypto';
import { existsSync, mkdirSync, readFileSync, readdirSync, renameSync, rmSync, statSync, writeFileSync } from 'node:fs';
import { createRequire } from 'node:module';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { parseArgs } from 'node:util';

const ROOT = join(dirname(fileURLToPath(import.meta.url)), '..');
const ENDPOINT = 'https://api.runpod.ai/v2/z-image-turbo';
const BASE_SEED = 20260930; // SyntheticData.RandomSeed
const MAX_ATTEMPTS = 3;
const POLL_INTERVAL_MS = 2000;
const POLL_LIMIT = 60;
const REQUEST_TIMEOUT_MS = 60_000;
const MAX_IMAGE_BYTES = 20_000_000;

let options;
try {
  ({ values: options } = parseArgs({
    options: {
      kind: { type: 'string' },
      count: { type: 'string', default: '400' },
      concurrency: { type: 'string', default: '4' },
      'dry-run': { type: 'boolean', default: false },
    },
  }));
} catch (error) {
  console.error(error.message);
  process.exit(2);
}
const count = Number(options.count);
const concurrency = Number(options.concurrency);
const dryRun = options['dry-run'];
if (!['faces', 'logos'].includes(options.kind)) {
  console.error('--kind must be faces or logos');
  process.exit(2);
}
if (!Number.isInteger(count) || count < 1 || !Number.isInteger(concurrency) || concurrency < 1) {
  console.error('--count and --concurrency must be positive integers');
  process.exit(2);
}

// ---------------------------------------------------------------------------------------------
// Faces. Arquebusiers are mostly men; ages follow the registry (minors from 16, some veterans over
// 60). Changing anything here changes every face: the cached raw faces would no longer match.
// ---------------------------------------------------------------------------------------------

// 3:4 and above the 600 x 800 minimum of NFR-15; one of the sizes the endpoint accepts.
const FACE_SOURCE = { width: 768, height: 1024 };
const FACE_OUTPUT = { width: 600, height: 800, quality: 72 };

const GENDERS = [
  { key: 'male', weight: 0.72, noun: { teen: 'teenage boy', adult: 'man' } },
  { key: 'female', weight: 0.28, noun: { teen: 'teenage girl', adult: 'woman' } },
];
const AGE_BANDS = [
  { key: '16-17', min: 16, max: 17, weight: 0.05 },
  { key: '18-29', min: 18, max: 29, weight: 0.25 },
  { key: '30-44', min: 30, max: 44, weight: 0.3 },
  { key: '45-59', min: 45, max: 59, weight: 0.28 },
  { key: '60-75', min: 60, max: 75, weight: 0.12 },
];
// Repeated entries weight the pick towards the most common looks in Alicante.
const SKIN = ['fair Mediterranean skin', 'fair Mediterranean skin', 'olive skin', 'olive skin', 'light tan skin', 'light tan skin', 'medium tan skin', 'fair skin with freckles'];
const EYES = ['brown eyes', 'brown eyes', 'brown eyes', 'dark brown eyes', 'dark brown eyes', 'dark brown eyes', 'hazel eyes', 'hazel eyes', 'green eyes', 'blue eyes'];
const HAIR_COLOUR = ['black', 'dark brown', 'chestnut brown', 'light brown', 'dark blond'];
const MALE_HAIR = ['short hair', 'short side-parted hair', 'buzz cut', 'short curly hair', 'medium-length wavy hair', 'short hair combed back'];
const FEMALE_HAIR = ['long straight hair', 'shoulder-length wavy hair', 'hair tied back in a ponytail', 'short bob haircut', 'long curly hair', 'hair in a low bun'];
const FACIAL_HAIR = ['clean-shaven', 'clean-shaven', 'light stubble', 'short trimmed beard', 'moustache and goatee', 'full short beard'];
const CLOTHING = ['a plain navy t-shirt', 'a plain black t-shirt', 'a white shirt', 'a grey crew-neck sweater', 'a dark green polo shirt', 'a light blue shirt', 'a burgundy sweater'];

// mulberry32: small deterministic PRNG, so the same --count always gives the same people.
function random(seed) {
  let state = seed >>> 0;
  return () => {
    state = (state + 0x6d2b79f5) >>> 0;
    let t = state;
    t = Math.imul(t ^ (t >>> 15), t | 1);
    t ^= t + Math.imul(t ^ (t >>> 7), t | 61);
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}

const pick = (next, list) => list[Math.floor(next() * list.length)];
const weighted = (next, list) => {
  let roll = next() * list.reduce((sum, item) => sum + item.weight, 0);
  return list.find((item) => (roll -= item.weight) < 0) ?? list.at(-1);
};

function describeFace(index) {
  const next = random(BASE_SEED + index);
  const gender = weighted(next, GENDERS);
  const band = weighted(next, AGE_BANDS);
  const age = band.min + Math.floor(next() * (band.max - band.min + 1));
  const isMale = gender.key === 'male';
  const greying = age >= 50 && next() < (age - 40) / 30;
  const hair = isMale && age >= 45 && next() < 0.25
    ? 'a shaved head'
    : `${greying ? 'greying' : pick(next, HAIR_COLOUR)} ${pick(next, isMale ? MALE_HAIR : FEMALE_HAIR)}`;
  const traits = [
    `${pick(next, SKIN)}`,
    `${pick(next, EYES)}`,
    hair,
    isMale && age >= 18 ? pick(next, FACIAL_HAIR) : null,
    age >= 40 && next() < 0.3 ? 'thin-framed glasses' : null,
    `wearing ${pick(next, CLOTHING)}`,
  ].filter(Boolean);
  const noun = age < 18 ? gender.noun.teen : gender.noun.adult;
  const prompt = [
    `Official ID card photo of a ${age}-year-old Spanish ${noun} from Alicante`,
    traits.join(', '),
    'head and shoulders, facing the camera straight on, neutral expression, mouth closed, eyes open',
    'plain light grey background, even soft studio lighting, sharp focus, natural skin texture',
    'realistic photograph, no text, no watermark, no jewellery',
  ].join('. ');
  const file = `face-${String(index + 1).padStart(4, '0')}.jpg`;
  return { file, raw: file, size: FACE_SOURCE, seed: BASE_SEED + index, prompt, entry: { file, gender: gender.key, ageBand: band.key, age, seed: BASE_SEED + index } };
}

// ---------------------------------------------------------------------------------------------
// Logos. One invented emblem per seeded comparsa but Abencerrajes, which the scenarios keep without
// a logo. Cruzados' must be dark enough to need the light tile in the dark theme. The motifs are
// generic heraldry; none copies a real comparsa's emblem (the maintainer reviews them, task 2.6).
// ---------------------------------------------------------------------------------------------

const LOGO_SOURCE = { width: 1024, height: 1024 };
const LOGO_OUTPUT_SIDE = 512;
const LOGO_MARGIN = 8;
const LOGOS = [
  ['Cruzados', 'a heater shield filled solid black, with a dark charcoal cross pattée and a dark grey sword pointing down, the whole emblem dark', 'only black, dark charcoal and dark grey; no white, no light colours anywhere inside the shield'],
  ['Hospitalarios', 'a shield with a cross fleury, an oil lamp and a laurel wreath', 'crimson red, silver grey and gold'],
  ['Zegríes', 'a round shield with a crescent moon, a five-pointed star and two crossed spears', 'emerald green, gold and white'],
  ['Tercios', 'a shield with crossed pikes and a morion helmet', 'red, gold and dark blue'],
  ['Ballesteros', 'a shield with two crossed crossbows and a quiver of bolts', 'royal blue and gold'],
  ['Corsarios', 'a shield with a galleon under full sail on waves', 'navy blue, white and gold'],
  ['Labradores', 'a shield with a wheat sheaf, a sickle and a rising sun', 'olive green, wheat yellow and brown'],
  ['Mozárabes', 'a shield with a bell tower between two olive branches', 'ochre, brick red and cream'],
  ['Caballeros de Sant Jordi', 'a shield with a knight\'s lance and a small green dragon', 'white, red and green'],
  ['Almirantes', 'a shield with an anchor over a compass rose', 'navy blue, gold and light blue'],
  ['Guardia del Rey', 'a shield with a royal crown and two crossed halberds', 'purple, gold and silver'],
  ['Almohades', 'a round shield with an eight-pointed geometric star', 'deep red, gold and white'],
  ['Nazaríes', 'a shield with a horseshoe arch and a palm tree', 'turquoise, gold and white'],
  ['Mudéjares', 'a shield with interlaced geometric brickwork and a small arch', 'terracotta, turquoise and cream'],
  ['Bereberes', 'a round shield with a camel and a desert sun', 'sand yellow, indigo and orange'],
  ['Beduinos', 'a shield with a tent under a crescent moon and stars', 'indigo, silver and sand'],
  ['Kábilas', 'a round shield with two crossed scimitars over a small round buckler', 'red, white and black outlines'],
  ['Califas', 'a shield with an ornate hanging lamp and a crescent', 'purple, gold and white'],
  ['Sarracenos', 'a shield with a horse head and a curved scimitar', 'crimson, gold and white'],
];

const slug = (name) => name.normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/^-|-$/g, '');

function describeLogo([comparsa, motif, colours], index) {
  const prompt = [
    `Flat heraldic emblem for a fictional Moors and Christians festival troupe: ${motif}`,
    `bold flat vector illustration, centred, thick clean outlines, limited palette of ${colours}`,
    'plain pure white background, no text, no letters, no words, no numbers, no banner, no ribbon, no watermark',
  ].join('. ');
  const file = `${slug(comparsa)}.png`;
  const seed = BASE_SEED + 5000 + index;
  return { file, raw: `${slug(comparsa)}.jpg`, size: LOGO_SOURCE, seed, prompt, entry: { comparsa, file, seed, prompt } };
}

// ---------------------------------------------------------------------------------------------
// Runpod.
// ---------------------------------------------------------------------------------------------

const sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms));

class UnreadableOutput extends Error {}

/** A request that would fail the same way for every image (bad key, bad input): stop, do not retry. */
class FatalRequest extends Error {}

async function call(apiKey, path, init) {
  const response = await fetch(`${ENDPOINT}${path}`, {
    ...init,
    signal: AbortSignal.timeout(REQUEST_TIMEOUT_MS),
    headers: { Authorization: `Bearer ${apiKey}`, 'Content-Type': 'application/json' },
  });
  if (!response.ok) {
    const message = `Runpod ${path} answered ${response.status}: ${(await response.text()).slice(0, 500)}`;
    // Only timeouts, throttling and server errors are worth another try.
    throw [408, 429].includes(response.status) || response.status >= 500 ? new Error(message) : new FatalRequest(message);
  }
  return response.json();
}

// The documented field is output.image_url; accept any URL or base64 image found in the output.
function findImage(value) {
  if (typeof value === 'string') {
    const isUrl = /^https?:\/\//.test(value);
    const isBase64 = value.startsWith('data:image/') || (value.length > 1000 && /^[A-Za-z0-9+/=\s]+$/.test(value));
    return isUrl || isBase64 ? value : null;
  }
  if (value && typeof value === 'object') {
    const preferred = ['image_url', 'image', 'url', 'result', 'images', 'output'];
    const keys = [...preferred.filter((key) => key in value), ...Object.keys(value).filter((key) => !preferred.includes(key))];
    for (const key of keys) {
      const found = findImage(value[key]);
      if (found) return found;
    }
  }
  return null;
}

// runsync may answer before the job ends (IN_QUEUE / IN_PROGRESS): poll its status until it does.
async function generate(apiKey, item) {
  const input = { prompt: item.prompt, size: `${item.size.width}*${item.size.height}`, seed: item.seed, output_format: 'jpeg', enable_safety_checker: true };
  let job = await call(apiKey, '/runsync', { method: 'POST', body: JSON.stringify({ input }) });
  for (let poll = 0; ['IN_QUEUE', 'IN_PROGRESS'].includes(job.status); poll++) {
    if (poll >= POLL_LIMIT) throw new Error(`job ${job.id} still ${job.status} after ${POLL_LIMIT} polls`);
    await sleep(POLL_INTERVAL_MS);
    job = await call(apiKey, `/status/${job.id}`, { method: 'GET' });
  }
  if (job.status !== 'COMPLETED') throw new Error(`job ${job.id} ended ${job.status}: ${job.error ?? 'no error given'}`);
  const source = findImage(job.output);
  if (!source) {
    // A completed job is billed: retrying would pay again for an answer the script cannot read.
    throw new UnreadableOutput(`job ${job.id} completed without an image the script recognises: ${JSON.stringify(job).slice(0, 2000)}`);
  }

  if (!source.startsWith('http')) {
    return Buffer.from(source.replace(/^data:[^,]*,/, ''), 'base64');
  }

  if (!source.startsWith('https://')) throw new Error(`job ${job.id} returned a non-HTTPS image URL`);
  // The image URL expires after 7 days: download it at once.
  const image = await fetch(source, { signal: AbortSignal.timeout(REQUEST_TIMEOUT_MS) });
  if (!image.ok) throw new Error(`download of job ${job.id} answered ${image.status}`);
  if (Number(image.headers.get('content-length') ?? 0) > MAX_IMAGE_BYTES) throw new Error(`job ${job.id} returned an image over ${MAX_IMAGE_BYTES} bytes`);
  const bytes = Buffer.from(await image.arrayBuffer());
  if (bytes.length > MAX_IMAGE_BYTES) throw new Error(`job ${job.id} returned an image over ${MAX_IMAGE_BYTES} bytes`);
  return bytes;
}

async function generateWithRetry(apiKey, item) {
  for (let attempt = 1; ; attempt++) {
    try {
      return await generate(apiKey, item);
    } catch (error) {
      if (attempt >= MAX_ATTEMPTS || error instanceof UnreadableOutput || error instanceof FatalRequest) throw error;
      console.warn(`${item.raw}: attempt ${attempt} failed (${error.message}), retrying`);
      await sleep(attempt * 3000);
    }
  }
}

/** What produced a raw image; a cached image is reused only while this is unchanged. */
const fingerprint = (item) => createHash('sha256').update(JSON.stringify({ prompt: item.prompt, seed: item.seed, size: item.size })).digest('hex');

function isCached(item, rawDir) {
  const raw = join(rawDir, item.raw);
  if (!existsSync(raw)) return false;
  const sidecar = `${raw}.json`;
  // Images cached before sidecars existed are trusted once and stamped with the current inputs.
  if (!existsSync(sidecar)) {
    writeFileSync(sidecar, JSON.stringify({ fingerprint: fingerprint(item) }));
    return true;
  }
  return JSON.parse(readFileSync(sidecar, 'utf8')).fingerprint === fingerprint(item);
}

async function fillCache(items, rawDir) {
  const missing = items.filter((item) => !isCached(item, rawDir));
  console.log(`${items.length - missing.length} raw images cached, ${missing.length} to generate`);
  if (missing.length === 0) return [];

  const apiKey = process.env.RUNPOD_API_KEY;
  if (!apiKey) {
    console.error('Set RUNPOD_API_KEY to generate the missing images (or pass --dry-run to only print the prompts)');
    process.exit(2);
  }

  mkdirSync(rawDir, { recursive: true });
  const failures = [];
  let cursor = 0;
  let stopped = false;
  async function worker() {
    while (!stopped && cursor < missing.length) {
      const item = missing[cursor++];
      try {
        const bytes = await generateWithRetry(apiKey, item);
        // Only a decodable image enters the cache, written whole: a crash mid-write leaves a .part file.
        await assertImage(bytes, item.raw);
        const target = join(rawDir, item.raw);
        writeFileSync(`${target}.part`, bytes);
        renameSync(`${target}.part`, target);
        writeFileSync(`${target}.json`, JSON.stringify({ fingerprint: fingerprint(item) }));
        console.log(`${item.raw} generated`);
      } catch (error) {
        failures.push(item.raw);
        console.error(`${item.raw}: ${error.message}`);
        // Every other job would come back the same way: stop paying for them.
        stopped ||= error instanceof UnreadableOutput || error instanceof FatalRequest;
      }
    }
  }
  await Promise.all(Array.from({ length: Math.min(concurrency, missing.length) }, worker));
  return failures;
}

// ---------------------------------------------------------------------------------------------
// Optimisation (sharp, from the frontend's dependencies). All metadata is dropped by sharp.
// ---------------------------------------------------------------------------------------------

async function assertImage(bytes, name) {
  if (bytes.length === 0) throw new Error(`${name}: the endpoint returned an empty image`);
  try {
    const { width, height } = await loadSharp()(bytes).metadata();
    if (!width || !height) throw new Error('no dimensions');
  } catch (error) {
    throw new Error(`${name}: the endpoint returned something that is not an image (${error.message})`);
  }
}

let sharpModule;
function loadSharp() {
  if (sharpModule) return sharpModule;
  try {
    sharpModule = createRequire(join(ROOT, 'frontend', 'package.json'))('sharp');
    return sharpModule;
  } catch {
    console.error('sharp is missing: run `npm ci` in frontend/ first');
    process.exit(2);
  }
}

async function optimiseFace(sharp, source, target) {
  await sharp(source)
    .resize(FACE_OUTPUT.width, FACE_OUTPUT.height)
    .jpeg({ quality: FACE_OUTPUT.quality, mozjpeg: true })
    .toFile(target);
}

/** Near-white pixels connected to the border become transparent; white inside the emblem stays. */
async function optimiseLogo(sharp, source, target) {
  const { data, info } = await sharp(source).ensureAlpha().raw().toBuffer({ resolveWithObject: true });
  const { width, height, channels } = info;
  const isBackground = (pixel) => {
    const offset = pixel * channels;
    return data[offset] > 232 && data[offset + 1] > 232 && data[offset + 2] > 232;
  };
  const seen = new Uint8Array(width * height);
  const stack = [];
  for (let x = 0; x < width; x++) stack.push(x, (height - 1) * width + x);
  for (let y = 0; y < height; y++) stack.push(y * width, y * width + width - 1);
  while (stack.length > 0) {
    const pixel = stack.pop();
    if (seen[pixel] || !isBackground(pixel)) continue;
    seen[pixel] = 1;
    data[pixel * channels + 3] = 0;
    const x = pixel % width;
    if (x > 0) stack.push(pixel - 1);
    if (x < width - 1) stack.push(pixel + 1);
    if (pixel >= width) stack.push(pixel - width);
    if (pixel < width * (height - 1)) stack.push(pixel + width);
  }
  const trimmed = await sharp(data, { raw: { width, height, channels } }).trim().png().toBuffer();
  await sharp(trimmed)
    .resize({ width: LOGO_OUTPUT_SIDE - 2 * LOGO_MARGIN, height: LOGO_OUTPUT_SIDE - 2 * LOGO_MARGIN, fit: 'inside' })
    // A transparent margin: the emblem never touches the edge, and the corners are always transparent.
    .extend({ top: LOGO_MARGIN, bottom: LOGO_MARGIN, left: LOGO_MARGIN, right: LOGO_MARGIN, background: { r: 0, g: 0, b: 0, alpha: 0 } })
    .png({ palette: true, colours: 64, effort: 10, compressionLevel: 9 })
    .toFile(target);
}

// ---------------------------------------------------------------------------------------------

const kind = options.kind;
const items = kind === 'faces' ? Array.from({ length: count }, (_, index) => describeFace(index)) : LOGOS.map(describeLogo);
if (dryRun) {
  for (const item of items) console.log(`${item.file} (seed ${item.seed}) ${item.prompt}`);
  process.exit(0);
}

const rawDir = join(ROOT, 'seed-assets', 'raw', kind);
const outDir = join(ROOT, 'backend', 'synthetic-data', kind);
const failures = await fillCache(items, rawDir);

if (failures.length > 0) {
  // Never replace the committed set with a partial one.
  console.error(`${failures.length} failed; ${outDir} is unchanged. Run the script again to retry them`);
  process.exit(1);
}

// Everything is written to a temporary folder that replaces the committed one only when complete.
const sharp = loadSharp();
const workDir = `${outDir}.tmp`;
rmSync(workDir, { recursive: true, force: true });
mkdirSync(workDir, { recursive: true });
for (const item of items) {
  const source = join(rawDir, item.raw);
  const target = join(workDir, item.file);
  try {
    await (kind === 'faces' ? optimiseFace(sharp, source, target) : optimiseLogo(sharp, source, target));
  } catch (error) {
    rmSync(workDir, { recursive: true, force: true });
    console.error(`${item.raw}: optimising failed (${error.message}); delete it from ${rawDir} to generate it again. ${outDir} is unchanged`);
    process.exit(1);
  }
}

const manifest = {
  generator: 'Z-Image Turbo (Tongyi-MAI, Apache-2.0) via the Runpod public endpoint; scripts/generate-seed-images.mjs',
  [kind]: items.map((item) => item.entry),
};
writeFileSync(join(workDir, 'manifest.json'), `${JSON.stringify(manifest, null, 2)}\n`);

// Swap in three steps, so a failure (Windows often holds a handle briefly) never leaves no folder.
const previous = `${outDir}.old`;
const retry = { recursive: true, force: true, maxRetries: 5, retryDelay: 200 };
rmSync(previous, retry);
if (existsSync(outDir)) renameSync(outDir, previous);
try {
  renameSync(workDir, outDir);
} catch (error) {
  if (existsSync(previous)) renameSync(previous, outDir);
  console.error(`Could not replace ${outDir} (${error.message}); it is unchanged and the new set is in ${workDir}`);
  process.exit(1);
}
rmSync(previous, retry);
const bytes = readdirSync(outDir).reduce((sum, name) => sum + statSync(join(outDir, name)).size, 0);
console.log(`${items.length} ${kind} written to ${outDir} (${(bytes / 1048576).toFixed(1)} MB)`);
