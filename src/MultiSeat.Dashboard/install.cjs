// Workaround for npm ERR_INVALID_ARG_VALUE caused by Steam's
// steam_master_ipc_name_override env var containing a null byte.
// This script spawns npm install with a sanitized environment.

const { spawnSync } = require("child_process");
const path = require("path");

// Strip null bytes from all env values rather than removing the var entirely.
// Removing vars like PATH would break child process spawning.
const cleanEnv = {};
for (const [k, v] of Object.entries(process.env)) {
  if (typeof v === "string") {
    const cleaned = v.replace(/\0/g, "");
    cleanEnv[k] = cleaned;
    if (cleaned !== v) {
      console.log(`Stripped null byte from env var: ${k}`);
    }
  }
}

const npmCli = path.join(
  path.dirname(process.execPath),
  "node_modules",
  "npm",
  "bin",
  "npm-cli.js"
);

console.log("Running npm install with sanitized env...");

const result = spawnSync(process.execPath, [npmCli, "install"], {
  stdio: "inherit",
  env: cleanEnv,
  cwd: __dirname,
});

process.exit(result.status || 0);
