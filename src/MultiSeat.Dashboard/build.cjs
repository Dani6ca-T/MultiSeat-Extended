// Build wrapper for MultiSeat Dashboard.
// Usage: node --require ./sanitize-env.cjs build.cjs
//    or: build.bat  (which adds --require automatically)
//
// The sanitize-env.cjs preload patches child_process.spawn to strip
// null bytes from env vars (caused by Steam's steam_master_ipc_name_override).

const { spawnSync } = require("child_process");
const path = require("path");

const cwd = __dirname;
const nodeExe = process.execPath;
const tsc = path.join(cwd, "node_modules", "typescript", "bin", "tsc");
const vite = path.join(cwd, "node_modules", "vite", "bin", "vite.js");
const sanitize = path.join(cwd, "sanitize-env.cjs");

// Propagate the sanitize-env preload to all child processes (vite spawns esbuild etc.)
const childEnv = {
  ...process.env,
  NODE_OPTIONS: `--require ${sanitize}`,
};

console.log("[build] Running tsc -b ...");
const tscResult = spawnSync(nodeExe, [tsc, "-b"], {
  stdio: "inherit",
  cwd,
  env: childEnv,
});
if (tscResult.status !== 0) {
  console.error("[build] tsc failed");
  process.exit(1);
}

console.log("[build] Running vite build ...");
const viteResult = spawnSync(nodeExe, [vite, "build"], {
  stdio: "inherit",
  cwd,
  env: childEnv,
});
if (viteResult.status !== 0) {
  console.error("[build] vite build failed");
  process.exit(1);
}

console.log("[build] Done!");
