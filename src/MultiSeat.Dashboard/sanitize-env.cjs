// Preload script: monkey-patch child_process.spawn/spawnSync to sanitize
// env vars containing null bytes (caused by Steam on Windows).
// Usage: node --require ./sanitize-env.cjs <script>

const cp = require("child_process");

function sanitizeEnv(env) {
  if (!env || typeof env !== "object") return env;
  const clean = {};
  for (const [k, v] of Object.entries(env)) {
    if (typeof v === "string" && v.includes("\0")) {
      clean[k] = v.replace(/\0/g, "");
    } else {
      clean[k] = v;
    }
  }
  return clean;
}

function patchOptions(args) {
  // spawn(cmd, args?, options?) — options is last object arg
  for (let i = args.length - 1; i >= 0; i--) {
    if (args[i] && typeof args[i] === "object" && !Array.isArray(args[i])) {
      if (args[i].env) {
        args[i] = { ...args[i], env: sanitizeEnv(args[i].env) };
      } else {
        args[i] = { ...args[i], env: sanitizeEnv(process.env) };
      }
      return args;
    }
  }
  // No options object found — append one
  args.push({ env: sanitizeEnv(process.env) });
  return args;
}

const origSpawn = cp.spawn;
const origSpawnSync = cp.spawnSync;
const origExecFile = cp.execFile;
const origExecFileSync = cp.execFileSync;
const origExec = cp.exec;
const origExecSync = cp.execSync;

cp.spawn = function (...args) {
  return origSpawn.apply(this, patchOptions(args));
};
cp.spawnSync = function (...args) {
  return origSpawnSync.apply(this, patchOptions(args));
};
cp.execFile = function (...args) {
  return origExecFile.apply(this, patchOptions(args));
};
cp.execFileSync = function (...args) {
  return origExecFileSync.apply(this, patchOptions(args));
};
cp.exec = function (...args) {
  return origExec.apply(this, patchOptions(args));
};
cp.execSync = function (...args) {
  return origExecSync.apply(this, patchOptions(args));
};
