import { execFileSync } from "node:child_process";

const dockerBin = process.env.WRANGLER_DOCKER_BIN || "docker";

function fail(message) {
  console.error(`\n[Docker preflight] ${message}\n`);
  process.exit(1);
}

try {
  execFileSync(dockerBin, ["--version"], {
    stdio: "ignore"
  });
} catch {
  fail(
    `Could not launch "${dockerBin}". Install Docker Desktop, or set WRANGLER_DOCKER_BIN to a Docker-compatible CLI before running Wrangler.`
  );
}

try {
  execFileSync(dockerBin, ["info"], {
    stdio: "ignore"
  });
} catch (error) {
  const details = [error.stdout, error.stderr]
    .filter(Boolean)
    .map((buffer) => buffer.toString().trim())
    .filter(Boolean)
    .join("\n");

  fail(
    [
      `Docker is installed, but the engine is not reachable via "${dockerBin} info".`,
      "Start Docker Desktop (or another Docker-compatible engine) and wait until `docker info` succeeds.",
      "If you use a non-default CLI or socket, set WRANGLER_DOCKER_BIN and/or DOCKER_HOST first.",
      details && `Docker said:\n${details}`
    ]
      .filter(Boolean)
      .join("\n")
  );
}

console.log(`[Docker preflight] Docker is available via "${dockerBin}".`);
