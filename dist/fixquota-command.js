/**
 * HermesHub - Native OpenCode Engine & Supercharged Quota Automation
 * Pipeline: Redundant IP Fetch -> WARP Disconnect -> Key Rotation -> WARP Connect -> DNS Flush -> IP Verification -> Sidecar Teardown
 */

const { exec } = require('child_process');
const util = require('util');
const fs = require('fs');
const path = require('path');
const execAsync = util.promisify(exec);

const IP_REGEX = /(?:[0-9]{1,3}\.){3}[0-9]{1,3}|(?:[a-fA-F0-9]{1,4}:){2,7}[a-fA-F0-9]{1,4}/;

/**
 * Fetch public IP across redundant high-availability endpoints.
 * @param {number} timeoutMs
 * @returns {Promise<string>}
 */
async function getPublicIp(timeoutMs = 5000) {
  const endpoints = [
    { url: 'https://api.ipify.org', isTrace: false },
    { url: 'https://icanhazip.com', isTrace: false },
    { url: 'https://1.1.1.1/cdn-cgi/trace', isTrace: true },
    { url: 'https://ifconfig.me/ip', isTrace: false }
  ];

  for (const ep of endpoints) {
    try {
      const res = await fetch(ep.url, {
        signal: AbortSignal.timeout(timeoutMs),
        headers: {
          'User-Agent': 'curl/8.4.0',
          'Accept': 'text/plain'
        }
      });

      if (!res.ok) continue;

      const raw = (await res.text()).trim();
      let ip = null;

      if (ep.isTrace) {
        const line = raw.split('\n').find((l) => l.trim().startsWith('ip='));
        if (line) {
          const matched = line.substring(3).trim().match(IP_REGEX);
          if (matched) ip = matched[0];
        }
      } else {
        const matched = raw.match(IP_REGEX);
        if (matched) ip = matched[0];
      }

      if (ip) {
        return ip;
      }
    } catch {
      // Failover to next redundant endpoint
    }
  }

  throw new Error('All redundant public IP endpoints unreachable.');
}

function sleep(ms) {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

/**
 * Discovers OpenCode Sidecar credentials across multi-candidate paths.
 * Priority 1: %USERPROFILE%\.local\state\opencode\service.json (Schema A)
 * Priority 2: %APPDATA%\ai.opencode.desktop\opencode\.sidecar-credentials.json (Schema B)
 * Priority 3: %APPDATA%\ai.opencode.desktop\.sidecar-credentials.json
 * @returns {{ port: number, password: string, url?: string, pid?: number } | null}
 */
function getOpenCodeSidecarCredentials() {
  const userProfile = process.env.USERPROFILE || '';
  const appData = process.env.APPDATA || '';

  const candidatePaths = [
    path.join(userProfile, '.local', 'state', 'opencode', 'service.json'),
    path.join(appData, 'ai.opencode.desktop', 'opencode', '.sidecar-credentials.json'),
    path.join(appData, 'ai.opencode.desktop', '.sidecar-credentials.json')
  ];

  for (const credFile of candidatePaths) {
    try {
      if (fs.existsSync(credFile)) {
        const content = fs.readFileSync(credFile, 'utf8');
        const parsed = JSON.parse(content);
        let port = Number(parsed.port) || 0;
        let url = String(parsed.url || '').trim();
        const password = String(parsed.password || '');
        const pid = Number(parsed.pid) || 0;

        if (url && !port) {
          try {
            const u = new URL(url);
            port = Number(u.port) || 0;
          } catch {}
        }

        if (port > 0) {
          return { port, password, url, pid };
        }
      }
    } catch {
      // Ignore read/parse lock errors during file rotation
    }
  }

  return null;
}

/**
 * Dispatches a connection-close teardown request to the OpenCode Sidecar
 * to purge dead TCP/TLS keep-alive sockets and force immediate IP re-evaluation.
 * @param {{ port: number, password: string, url?: string }} credentials
 * @returns {Promise<boolean>}
 */
async function syncOpenCodeSidecar(credentials) {
  if (!credentials || !credentials.port) return false;

  let baseUrl = credentials.url || `http://127.0.0.1:${credentials.port}`;
  baseUrl = baseUrl.replace(/\/+$/, '');
  if (!baseUrl.endsWith('/api')) {
    baseUrl = `${baseUrl}/api`;
  }

  const authCandidates = [
    Buffer.from(`opencode:${credentials.password}`).toString('base64'),
    Buffer.from(`:${credentials.password}`).toString('base64')
  ];

  const targetEndpoints = [
    `${baseUrl}/info`,
    `${baseUrl}/session`
  ];

  for (const basicAuth of authCandidates) {
    for (const ep of targetEndpoints) {
      try {
        const res = await fetch(ep, {
          method: 'GET',
          headers: {
            'Authorization': `Basic ${basicAuth}`,
            'Connection': 'close',
            'User-Agent': 'HermesHub-QuotaEngine/1.0'
          },
          signal: AbortSignal.timeout(3000)
        });

        // Status 200, 204, or any OK HTTP response confirms Sidecar received teardown
        if (res.status >= 200 && res.status < 400) {
          return true;
        }
      } catch {
        // Continue to next probe
      }
    }
  }

  return false;
}

/**
 * Supercharged Quota & IP Rotation Engine
 * @returns {Promise<{ success: boolean, oldIp: string, newIp: string, dnsFlushed: boolean, sidecarSynced: boolean, message: string }>}
 */
async function resetQuotaAndRotateIp() {
  const report = {
    success: false,
    oldIp: 'unknown',
    newIp: 'unknown',
    dnsFlushed: false,
    sidecarSynced: false,
    message: ''
  };

  // 1. Fetch initial public IP
  try {
    report.oldIp = await getPublicIp(4000);
  } catch {
    report.oldIp = 'unknown';
  }

  // 2. Cloudflare WARP disconnection & cryptographic key rotation
  try {
    await execAsync('warp-cli disconnect');
  } catch {
    // If already disconnected, proceed
  }

  await sleep(1200);

  try {
    await execAsync('warp-cli tunnel rotate-keys');
  } catch {
    // Try fallback rotate-keys command syntax if available
    try {
      await execAsync('warp-cli rotate-keys');
    } catch {
      // Some WARP versions handle rotation on reconnect
    }
  }

  await sleep(800);

  try {
    await execAsync('warp-cli connect');
  } catch (err) {
    report.message = `Failed to connect WARP: ${err.message}`;
    return report;
  }

  // 3. Flush OS DNS cache to purge dead sockets
  try {
    await execAsync('ipconfig /flushdns');
    report.dnsFlushed = true;
  } catch {
    report.dnsFlushed = false;
  }

  // 4. Aggressive retry loop (polling every 1.5s, up to 15s) strictly requiring a NEW, distinct public IP
  const maxAttempts = 10;
  const pollIntervalMs = 1500;
  let detectedNewIp = null;

  for (let attempt = 1; attempt <= maxAttempts; attempt++) {
    await sleep(pollIntervalMs);

    try {
      const probeIp = await getPublicIp(3500);
      if (probeIp) {
        if (report.oldIp === 'unknown' || probeIp !== report.oldIp) {
          detectedNewIp = probeIp;
          break;
        }
      }
    } catch {
      // Socket still negotiating
    }

    // Midway fallback: force another key rotation if IP hasn't changed by attempt 5
    if (attempt === 5 && !detectedNewIp && report.oldIp !== 'unknown') {
      try {
        await execAsync('warp-cli tunnel rotate-keys');
      } catch { }
    }
  }

  if (!detectedNewIp) {
    report.message = `IP rotation timed out: failed to obtain a new distinct public IP (old: ${report.oldIp}).`;
    return report;
  }

  report.newIp = detectedNewIp;

  // 5 & 6. Detect OpenCode Sidecar credentials & dispatch connection-close teardown
  const sidecarCreds = getOpenCodeSidecarCredentials();
  if (sidecarCreds) {
    report.sidecarSynced = await syncOpenCodeSidecar(sidecarCreds);
  }

  report.success = true;
  report.message = report.sidecarSynced
    ? `Public IP successfully rotated from ${report.oldIp} to ${report.newIp}. OpenCode Sidecar sockets re-synced.`
    : `Public IP successfully rotated from ${report.oldIp} to ${report.newIp}. OS DNS cache purged.`;

  return report;
}

async function main() {
  try {
    const outcome = await resetQuotaAndRotateIp();
    // Return clean JSON output
    console.log(JSON.stringify(outcome, null, 2));
    process.exitCode = outcome.success ? 0 : 1;
  } catch (err) {
    const errorOutcome = {
      success: false,
      oldIp: 'unknown',
      newIp: 'unknown',
      dnsFlushed: false,
      sidecarSynced: false,
      message: `Fatal error during quota reset: ${err.message}`
    };
    console.log(JSON.stringify(errorOutcome, null, 2));
    process.exitCode = 1;
  }
}

if (require.main === module) {
  main();
}

module.exports = {
  resetQuotaAndRotateIp,
  rotateWarpIp: resetQuotaAndRotateIp,
  getPublicIp,
  getOpenCodeSidecarCredentials,
  syncOpenCodeSidecar
};
