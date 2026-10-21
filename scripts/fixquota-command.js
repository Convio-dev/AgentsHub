/**
 * AgentsHub - Native OpenCode Engine & Supercharged Quota Automation
 * Pipeline: Redundant IP Fetch -> WARP Disconnect -> Key Rotation -> WARP Connect -> DNS Flush -> IP Verification -> Sidecar Teardown
 */

const { exec } = require('child_process');
const util = require('util');
const fs = require('fs');
const path = require('path');
const execAsync = util.promisify(exec);

const IPV4_REGEX = /^(?:[0-9]{1,3}\.){3}[0-9]{1,3}$/;
const IPV6_REGEX = /^[a-fA-F0-9:]{3,39}$/;
const IP_REGEX = /(?:[0-9]{1,3}\.){3}[0-9]{1,3}|(?:[a-fA-F0-9]{1,4}:){2,7}[a-fA-F0-9]{1,4}/;

/**
 * Fetch dedicated public IPv4 address.
 * @param {number} timeoutMs
 * @returns {Promise<string|null>}
 */
async function getIpv4(timeoutMs = 4000) {
  const endpoints = [
    { url: 'https://api4.ipify.org', isTrace: false },
    { url: 'https://v4.ident.me', isTrace: false },
    { url: 'https://1.1.1.1/cdn-cgi/trace', isTrace: true }
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
          const cand = line.substring(3).trim();
          if (IPV4_REGEX.test(cand)) ip = cand;
        }
      } else {
        const cand = raw.split('\n')[0].trim();
        if (IPV4_REGEX.test(cand)) ip = cand;
      }

      if (ip) return ip;
    } catch {
      // Continue to next endpoint
    }
  }

  return null;
}

/**
 * Fetch dedicated public IPv6 address. Gracefully returns null if IPv6 is unsupported/unassigned.
 * @param {number} timeoutMs
 * @returns {Promise<string|null>}
 */
async function getIpv6(timeoutMs = 4000) {
  const endpoints = [
    { url: 'https://api6.ipify.org', isTrace: false },
    { url: 'https://v6.ident.me', isTrace: false }
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
      const cand = raw.split('\n')[0].trim();
      if (IPV6_REGEX.test(cand) && cand.includes(':')) {
        return cand;
      }
    } catch {
      // Non-blocking graceful catch if IPv6 is unreachable
    }
  }

  return null;
}

/**
 * Fetch both protocol families (IPv4 and IPv6) concurrently.
 * @param {number} timeoutMs
 * @returns {Promise<{ ipv4: string, ipv6: string }>}
 */
async function getDualStackIps(timeoutMs = 4000) {
  const [v4Result, v6Result] = await Promise.allSettled([
    getIpv4(timeoutMs),
    getIpv6(timeoutMs)
  ]);

  const ipv4 = (v4Result.status === 'fulfilled' && v4Result.value) ? v4Result.value : 'unknown';
  const ipv6 = (v6Result.status === 'fulfilled' && v6Result.value) ? v6Result.value : 'Not assigned';

  return { ipv4, ipv6 };
}

/**
 * Fetch public IP across redundant high-availability endpoints.
 * @param {number} timeoutMs
 * @returns {Promise<string>}
 */
async function getPublicIp(timeoutMs = 5000) {
  const dual = await getDualStackIps(timeoutMs);
  if (dual.ipv4 !== 'unknown') return dual.ipv4;
  if (dual.ipv6 !== 'Not assigned') return dual.ipv6;

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
            'User-Agent': 'AgentsHub-QuotaEngine/1.0'
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
 * Converts 2-letter ISO country code into regional indicator flag emoji.
 * @param {string} code
 * @returns {string}
 */
function countryCodeToFlag(code) {
  if (!code || code.length !== 2) return '🌐';
  try {
    return String.fromCodePoint(...code.toUpperCase().split('').map(c => 127397 + c.charCodeAt(0)));
  } catch {
    return '🌐';
  }
}

/**
 * Resolves Geolocation details (countryCode, country, countryEmoji, city).
 * @param {string} ip
 * @returns {Promise<{ countryCode: string, country: string, countryEmoji: string, city: string }>}
 */
async function getIpGeoDetails(ip) {
  const geo = {
    countryCode: '',
    country: '',
    countryEmoji: '🌐',
    city: ''
  };

  // 1. Primary: ipwho.is
  try {
    const res = await fetch(`https://ipwho.is/${ip || ''}`, { signal: AbortSignal.timeout(3000) });
    if (res.ok) {
      const data = await res.json();
      if (data && data.success !== false) {
        geo.countryCode = data.country_code || '';
        geo.country = data.country || '';
        geo.city = data.city || '';
        geo.countryEmoji = countryCodeToFlag(geo.countryCode);
        return geo;
      }
    }
  } catch {}

  // 2. Fallback: 1.1.1.1/cdn-cgi/trace
  try {
    const res = await fetch('https://1.1.1.1/cdn-cgi/trace', { signal: AbortSignal.timeout(3000) });
    if (res.ok) {
      const text = await res.text();
      const locMatch = text.match(/loc=([A-Z]{2})/i);
      const coloMatch = text.match(/colo=([A-Z]{3})/i);
      if (locMatch) {
        geo.countryCode = locMatch[1].toUpperCase();
        geo.countryEmoji = countryCodeToFlag(geo.countryCode);
        try {
          const regionNames = new Intl.DisplayNames(['en'], { type: 'region' });
          geo.country = regionNames.of(geo.countryCode) || geo.countryCode;
        } catch {
          geo.country = geo.countryCode;
        }
      }
      if (coloMatch) {
        geo.city = coloMatch[1].toUpperCase();
      }
      return geo;
    }
  } catch {}

  return geo;
}

/**
 * Discovers Proton VPN executable across common install paths.
 * @returns {string|null}
 */
function findProtonExecutable() {
  const programFiles = process.env.ProgramFiles || 'C:\\Program Files';
  const programFilesX86 = process.env['ProgramFiles(x86)'] || 'C:\\Program Files (x86)';
  const localAppData = process.env.LOCALAPPDATA || '';

  const candidates = [
    path.join(programFiles, 'Proton', 'VPN', 'ProtonVPN.Launcher.exe'),
    path.join(programFiles, 'Proton', 'VPN', 'ProtonVPN.exe'),
    path.join(localAppData, 'Proton', 'VPN', 'ProtonVPN.Launcher.exe'),
    path.join(localAppData, 'ProtonVPN', 'ProtonVPN.exe'),
    path.join(programFilesX86, 'Proton', 'VPN', 'ProtonVPN.Launcher.exe')
  ];

  const protonBase = path.join(programFiles, 'Proton', 'VPN');
  if (fs.existsSync(protonBase)) {
    try {
      const items = fs.readdirSync(protonBase).filter(d => d.startsWith('v')).sort().reverse();
      for (const item of items) {
        candidates.push(path.join(protonBase, item, 'ProtonVPN.Client.exe'));
      }
    } catch { }
  }

  for (const c of candidates) {
    if (fs.existsSync(c)) return c;
  }

  return null;
}

/**
 * Checks if Proton VPN process is currently running.
 * @returns {boolean}
 */
function isProtonRunning() {
  try {
    const out = child_process.execSync('tasklist /FI "IMAGENAME eq ProtonVPN.Client.exe" /NH', { encoding: 'utf8', timeout: 2000 });
    if (out.includes('ProtonVPN.Client')) return true;
    const out2 = child_process.execSync('tasklist /FI "IMAGENAME eq ProtonVPN.exe" /NH', { encoding: 'utf8', timeout: 2000 });
    return out2.includes('ProtonVPN');
  } catch {
    return false;
  }
}

/**
 * Spawns Proton VPN GUI without blocking Node execution.
 * @returns {boolean}
 */
function launchProtonGui() {
  const exe = findProtonExecutable();
  if (!exe) return false;
  const dir = path.dirname(exe);
  try {
    const child = child_process.spawn('explorer.exe', [exe], {
      detached: true,
      cwd: dir,
      stdio: 'ignore'
    });
    child.unref();
    return true;
  } catch {
    try {
      const child = child_process.spawn('cmd.exe', ['/c', 'start', '""', exe], {
        detached: true,
        cwd: dir,
        stdio: 'ignore'
      });
      child.unref();
      return true;
    } catch {
      return false;
    }
  }
}

/**
 * Cleanly disconnects any active VPN tunnel (WARP and Proton) and returns to direct connection.
 */
async function disconnectTunnel() {
  const report = {
    success: false,
    provider: 'Direct',
    old: { ipv4: 'unknown', ipv6: 'Not assigned' },
    new: { ipv4: 'unknown', ipv6: 'Not assigned' },
    oldIp: 'unknown',
    newIp: 'unknown',
    countryCode: '',
    country: '',
    countryEmoji: '🌐',
    city: '',
    dnsFlushed: false,
    sidecarSynced: false,
    message: ''
  };

  try {
    const initialDual = await getDualStackIps(3000);
    report.old = initialDual;
    report.oldIp = initialDual.ipv4 !== 'unknown' ? initialDual.ipv4 : initialDual.ipv6;
  } catch { }

  // Disconnect WARP
  try {
    await execAsync('warp-cli disconnect');
  } catch { }

  // Trigger Proton URI disconnect gracefully if supported
  try {
    child_process.spawn('cmd', ['/c', 'start', 'protonvpn://disconnect'], { detached: true, stdio: 'ignore' }).unref();
  } catch { }

  // Flush DNS
  try {
    await execAsync('ipconfig /flushdns');
    report.dnsFlushed = true;
  } catch { }

  await sleep(1500);

  // Probe direct ISP IP
  try {
    const directDual = await getDualStackIps(3500);
    report.new = directDual;
    report.newIp = directDual.ipv4 !== 'unknown' ? directDual.ipv4 : directDual.ipv6;
    report.newIpv4 = directDual.ipv4;
    report.newIpv6 = directDual.ipv6;

    const targetIp = (directDual.ipv4 !== 'unknown' ? directDual.ipv4 : directDual.ipv6);
    const geo = await getIpGeoDetails(targetIp);
    report.countryCode = geo.countryCode;
    report.country = geo.country;
    report.countryEmoji = geo.countryEmoji;
    report.city = geo.city;

    // Sidecar sync
    const sidecarCreds = getOpenCodeSidecarCredentials();
    if (sidecarCreds) {
      report.sidecarSynced = await syncOpenCodeSidecar(sidecarCreds);
    }

    report.success = true;
    report.message = `VPN disconnected. Restored direct connection to ${report.newIp} (${report.country || 'ISP'})`;
  } catch (err) {
    report.message = `Disconnected, but IP verification failed: ${err.message}`;
  }

  return report;
}

/**
 * Supercharged Proton VPN Server Rotation Pipeline with 14s timeout and graceful fallback
 */
async function rotateProtonIp() {
  const report = {
    success: false,
    provider: 'ProtonVPN',
    cooldownSeconds: 60,
    old: { ipv4: 'unknown', ipv6: 'Not assigned' },
    new: { ipv4: 'unknown', ipv6: 'Not assigned' },
    oldIp: 'unknown',
    newIp: 'unknown',
    oldIpv4: 'unknown',
    oldIpv6: 'Not assigned',
    newIpv4: 'unknown',
    newIpv6: 'Not assigned',
    countryCode: '',
    country: '',
    countryEmoji: '🌐',
    city: '',
    dnsFlushed: false,
    sidecarSynced: false,
    message: ''
  };

  try {
    const initialDual = await getDualStackIps(3000);
    report.old = initialDual;
    report.oldIpv4 = initialDual.ipv4;
    report.oldIpv6 = initialDual.ipv6;
    report.oldIp = initialDual.ipv4 !== 'unknown' ? initialDual.ipv4 : initialDual.ipv6;
  } catch { }

  // 1. Check if Proton GUI is running; if not, launch it in background
  if (!isProtonRunning()) {
    launchProtonGui();
    await sleep(2000);
  }

  // 2. Trigger connection attempt via URI scheme (non-blocking)
  try {
    child_process.spawn('cmd', ['/c', 'start', 'protonvpn://connect'], { detached: true, stdio: 'ignore' }).unref();
  } catch { }

  // 3. Flush DNS
  try {
    await execAsync('ipconfig /flushdns');
    report.dnsFlushed = true;
  } catch { }

  // 4. Polling loop for new IP (max 14 seconds: 8 attempts x 1.5s)
  let detectedNewDual = null;
  for (let attempt = 1; attempt <= 8; attempt++) {
    await sleep(1500);
    try {
      const probe = await getDualStackIps(2500);
      const hasV4 = probe.ipv4 && probe.ipv4 !== 'unknown';
      const hasV6 = probe.ipv6 && probe.ipv6 !== 'Not assigned';

      const v4Changed = hasV4 && (report.old.ipv4 === 'unknown' || probe.ipv4 !== report.old.ipv4);
      const v6Changed = hasV6 && (report.old.ipv6 === 'Not assigned' || probe.ipv6 !== report.old.ipv6);

      if (v4Changed || v6Changed) {
        detectedNewDual = probe;
        break;
      }
    } catch { }
  }

  if (detectedNewDual) {
    report.success = true;
    report.new = detectedNewDual;
    report.newIpv4 = detectedNewDual.ipv4;
    report.newIpv6 = detectedNewDual.ipv6;
    report.newIp = detectedNewDual.ipv4 !== 'unknown' ? detectedNewDual.ipv4 : detectedNewDual.ipv6;
    report.ip = report.newIp;

    const targetIpForGeo = (detectedNewDual.ipv4 !== 'unknown' ? detectedNewDual.ipv4 : detectedNewDual.ipv6);
    const geo = await getIpGeoDetails(targetIpForGeo);
    report.countryCode = geo.countryCode;
    report.country = geo.country;
    report.countryEmoji = geo.countryEmoji;
    report.city = geo.city;

    const sidecarCreds = getOpenCodeSidecarCredentials();
    if (sidecarCreds) {
      report.sidecarSynced = await syncOpenCodeSidecar(sidecarCreds);
    }

    report.message = `Proton VPN tunnel active. New IP: ${report.newIp} (${report.country || 'Proton'})`;
  } else {
    // Timeout reached without new IP
    report.success = false;
    report.new = report.old;
    report.newIpv4 = report.oldIpv4;
    report.newIpv6 = report.oldIpv6;
    report.newIp = report.oldIp;
    report.ip = report.oldIp;
    report.message = "Proton VPN did not establish a new tunnel within 14s. Please click 'Connect' in the Proton VPN window, then click Reset.";
  }

  return report;
}

/**
 * Supercharged Quota & IP Rotation Engine with Dual-Stack Resolution
 * @returns {Promise<{ success: boolean, provider: string, cooldownSeconds: number, old: { ipv4: string, ipv6: string }, new: { ipv4: string, ipv6: string }, oldIp: string, newIp: string, oldIpv4: string, oldIpv6: string, newIpv4: string, newIpv6: string, countryCode: string, country: string, countryEmoji: string, city: string, dnsFlushed: boolean, sidecarSynced: boolean, message: string }>}
 */
async function resetQuotaAndRotateIp() {
  const report = {
    success: false,
    provider: 'CloudflareWARP',
    cooldownSeconds: 12,
    old: { ipv4: 'unknown', ipv6: 'Not assigned' },
    new: { ipv4: 'unknown', ipv6: 'Not assigned' },
    oldIp: 'unknown',
    newIp: 'unknown',
    oldIpv4: 'unknown',
    oldIpv6: 'Not assigned',
    newIpv4: 'unknown',
    newIpv6: 'Not assigned',
    countryCode: '',
    country: '',
    countryEmoji: '🌐',
    city: '',
    dnsFlushed: false,
    sidecarSynced: false,
    message: ''
  };

  // 1. Fetch initial dual-stack public IPs before rotation
  try {
    const initialDual = await getDualStackIps(4000);
    report.old = initialDual;
    report.oldIpv4 = initialDual.ipv4;
    report.oldIpv6 = initialDual.ipv6;
    report.oldIp = initialDual.ipv4 !== 'unknown' ? initialDual.ipv4 : (initialDual.ipv6 !== 'Not assigned' ? initialDual.ipv6 : 'unknown');
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
    try {
      await execAsync('warp-cli rotate-keys');
    } catch { }
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
  let detectedNewDual = null;

  for (let attempt = 1; attempt <= maxAttempts; attempt++) {
    await sleep(pollIntervalMs);

    try {
      const probeDual = await getDualStackIps(3500);
      const hasV4 = probeDual.ipv4 && probeDual.ipv4 !== 'unknown';
      const hasV6 = probeDual.ipv6 && probeDual.ipv6 !== 'Not assigned';

      const v4Changed = hasV4 && (report.old.ipv4 === 'unknown' || probeDual.ipv4 !== report.old.ipv4);
      const v6Changed = hasV6 && (report.old.ipv6 === 'Not assigned' || probeDual.ipv6 !== report.old.ipv6);

      if (v4Changed || v6Changed || (hasV4 && report.old.ipv4 === 'unknown')) {
        detectedNewDual = probeDual;
        break;
      }
    } catch { }

    if (attempt === 5 && !detectedNewDual) {
      try {
        await execAsync('warp-cli tunnel rotate-keys');
      } catch { }
    }
  }

  if (!detectedNewDual) {
    report.message = `IP rotation timed out: failed to obtain a new distinct public IP (old: ${report.oldIp}).`;
    return report;
  }

  report.new = detectedNewDual;
  report.newIpv4 = detectedNewDual.ipv4;
  report.newIpv6 = detectedNewDual.ipv6;
  report.newIp = detectedNewDual.ipv4 !== 'unknown' ? detectedNewDual.ipv4 : detectedNewDual.ipv6;
  report.ip = report.newIp;

  const targetIpForGeo = (detectedNewDual.ipv4 !== 'unknown' ? detectedNewDual.ipv4 : detectedNewDual.ipv6);
  const geo = await getIpGeoDetails(targetIpForGeo);
  report.countryCode = geo.countryCode;
  report.country = geo.country;
  report.countryEmoji = geo.countryEmoji;
  report.city = geo.city;

  const sidecarCreds = getOpenCodeSidecarCredentials();
  if (sidecarCreds) {
    report.sidecarSynced = await syncOpenCodeSidecar(sidecarCreds);
  }

  report.success = true;
  report.message = 'Dual-stack rotated successfully';

  return report;
}

async function main() {
  const args = process.argv.slice(2);
  const isDisconnect = args.includes('--disconnect') || args.includes('-d');
  const providerArg = args.find(a => a.startsWith('--provider='));
  const provider = providerArg ? providerArg.split('=')[1].toLowerCase() : (args.includes('proton') ? 'proton' : 'warp');

  try {
    let outcome;
    if (isDisconnect) {
      outcome = await disconnectTunnel();
    } else if (provider === 'proton') {
      outcome = await rotateProtonIp();
    } else {
      outcome = await resetQuotaAndRotateIp();
    }

    console.log(JSON.stringify(outcome, null, 2));
    process.exitCode = outcome.success ? 0 : 1;
  } catch (err) {
    const errorOutcome = {
      success: false,
      provider: provider === 'proton' ? 'ProtonVPN' : 'CloudflareWARP',
      cooldownSeconds: provider === 'proton' ? 60 : 12,
      old: { ipv4: 'unknown', ipv6: 'Not assigned' },
      new: { ipv4: 'unknown', ipv6: 'Not assigned' },
      oldIp: 'unknown',
      newIp: 'unknown',
      dnsFlushed: false,
      sidecarSynced: false,
      message: `Fatal error during network operation: ${err.message}`
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
  rotateProtonIp,
  disconnectTunnel,
  getPublicIp,
  getIpv4,
  getIpv6,
  getDualStackIps,
  getOpenCodeSidecarCredentials,
  syncOpenCodeSidecar,
  findProtonExecutable
};

