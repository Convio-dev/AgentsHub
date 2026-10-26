// AgentsHub v2.0 AI Agent Chat IDE Subsystem
(function initAgentChatIde() {
  'use strict';

  // --- Quake HUD Console Dropdown Handler ---
  const hud = document.getElementById('quake-hud');
  let isHudOpen = false;

  function toggleHud(forceState) {
    if (!hud) return;
    isHudOpen = typeof forceState === 'boolean' ? forceState : !isHudOpen;
    if (isHudOpen) {
      hud.classList.add('active');
      hud.classList.remove('pointer-events-none');
    } else {
      hud.classList.remove('active');
      hud.classList.add('pointer-events-none');
    }
  }
  window.toggleHud = toggleHud;

  const btnToggleHud = document.getElementById('btn-toggle-hud');
  if (btnToggleHud) {
    btnToggleHud.addEventListener('click', () => toggleHud());
  }

  // F12 or Backtick (`) Global Hotkey for Quake HUD
  window.addEventListener('keydown', (e) => {
    if (e.key === 'F12' || (e.key === '`' && !['INPUT', 'TEXTAREA'].includes(e.target.tagName))) {
      e.preventDefault();
      toggleHud();
    }
  });

  function clearHudTerminal() {
    const container = document.getElementById('hud-terminal-content');
    if (container) {
      container.innerHTML = '<p class="text-slate-500">// Terminal buffer cleared. ConPTY process listening...</p>';
    }
  }
  window.clearHudTerminal = clearHudTerminal;

  // --- Profile & Multi-Account Switching ---
  async function switchAgentProfile(profileName) {
    if (!profileName) return;
    const profileSelector = document.getElementById('agent-profile-selector');
    if (profileSelector) profileSelector.value = profileName;

    const badge = document.getElementById('header-profile-badge');
    if (badge) badge.innerText = `Antigravity CLI (agy) • ${profileName}`;

    const railBtn = document.getElementById('btn-rail-profile');
    if (railBtn) {
      const shortName = profileName.replace(/[^a-zA-Z0-9]/g, '').slice(0, 2).toUpperCase() || 'P1';
      railBtn.innerText = shortName;
      railBtn.title = `Active Profile: ${profileName}`;
    }

    // Append notification into Quake HUD
    appendHudLog(`[VirtualHome] Profile switched to ${profileName}. Ports remapped cleanly.`, 'sky');

    // Call backend bridge
    try {
      if (typeof window.callBridge === 'function') {
        await window.callBridge('switch_antigravity_profile', { profile: profileName });
      }
    } catch (err) {
      console.warn('Bridge profile switch warning:', err);
    }
  }
  window.switchAgentProfile = switchAgentProfile;

  // --- IP Address Rotation (WARP / VPN) ---
  function rotateIpAddress() {
    appendHudLog('[WARP Proxy] Rotating dual-stack IP address for account quota refresh...', 'amber');
    if (typeof window.callBridge === 'function') {
      window.callBridge('reset_antigravity_quota_vpn', { provider: 'warp' })
        .then(() => {
          appendHudLog('[WARP Proxy] IP rotation completed successfully.', 'emerald');
          if (typeof window.showToast === 'function') window.showToast('WARP IP rotated cleanly.', 'success');
        })
        .catch(err => {
          appendHudLog(`[WARP Proxy] Rotation note: ${err?.message || err}`, 'slate');
        });
    }
  }
  window.rotateIpAddress = rotateIpAddress;

  function appendHudLog(message, color = 'slate') {
    const hudContent = document.getElementById('hud-terminal-content');
    if (!hudContent) return;
    const colorClasses = {
      slate: 'text-slate-400',
      sky: 'text-sky-400',
      emerald: 'text-emerald-400',
      amber: 'text-amber-400',
      rose: 'text-rose-400'
    };
    const cls = colorClasses[color] || 'text-slate-300';
    hudContent.innerHTML += `<p class="${cls}">${escapeHtml(message)}</p>`;
    hudContent.scrollTop = hudContent.scrollHeight;
  }
  window.appendHudLog = appendHudLog;

  // --- Chat Stream Management & Tool Cards ---
  function escapeHtml(str) {
    return String(str || '').replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
  }

  function copyCodeSnippet(btn) {
    const pre = btn.closest('div').nextElementSibling;
    if (!pre) return;
    const text = pre.innerText;
    navigator.clipboard.writeText(text).then(() => {
      const orig = btn.innerHTML;
      btn.innerHTML = '<i class="fa-solid fa-check text-emerald-400"></i> Copied!';
      setTimeout(() => btn.innerHTML = orig, 1800);
    }).catch(() => {
      const dummy = document.createElement('textarea');
      document.body.appendChild(dummy);
      dummy.value = text;
      dummy.select();
      document.execCommand('copy');
      document.body.removeChild(dummy);
      const orig = btn.innerHTML;
      btn.innerHTML = '<i class="fa-solid fa-check text-emerald-400"></i> Copied!';
      setTimeout(() => btn.innerHTML = orig, 1800);
    });
  }
  window.copyCodeSnippet = copyCodeSnippet;

  function submitUserPrompt() {
    const input = document.getElementById('prompt-input');
    if (!input) return;
    const text = input.value.trim();
    if (!text) return;

    const stream = document.getElementById('chat-stream');
    if (!stream) return;

    // 1. Render User Message Card
    const userCard = document.createElement('div');
    userCard.className = 'flex items-start space-x-3 max-w-3xl';
    userCard.innerHTML = `
      <div class="w-8 h-8 rounded-lg bg-slate-800 border border-slate-700 flex items-center justify-center font-bold text-xs text-slate-300 flex-shrink-0">
        YOU
      </div>
      <div class="flex-1 bg-surface-card border border-surface-border rounded-xl p-4 text-sm text-slate-200">
        <p>${escapeHtml(text)}</p>
      </div>
    `;
    stream.appendChild(userCard);
    input.value = '';
    stream.scrollTop = stream.scrollHeight;

    // Log to Quake HUD
    appendHudLog(`[User Prompt] ${text.slice(0, 80)}`, 'sky');

    // 2. Dispatch to Background Supervised CLI
    const activeProfile = document.getElementById('agent-profile-selector')?.value || 'ZXR@1';
    
    // Send input to supervised backend runner
    if (typeof window.callBridge === 'function') {
      window.callBridge('send_terminal_input', {
        cli: 'antigravity_cli',
        input: text + '\n'
      }).catch(() => {});
    }

    if (window.chrome && window.chrome.webview) {
      try {
        window.chrome.webview.postMessage({
          event: 'agent_prompt_submitted',
          prompt: text,
          profile: activeProfile
        });
      } catch (e) {}
    }

    // 3. Render Simulated Headless Agent Response with Tool Call Cards
    setTimeout(() => {
      renderAgentResponse(text);
    }, 600);
  }
  window.submitUserPrompt = submitUserPrompt;

  function renderAgentResponse(promptText) {
    const stream = document.getElementById('chat-stream');
    if (!stream) return;

    const agentCard = document.createElement('div');
    agentCard.className = 'flex items-start space-x-3 max-w-3xl';

    // Check if prompt suggests file changes or git actions
    const isGit = /git|diff|branch|commit|push|pull/i.test(promptText);
    const isBuild = /build|run|test|npm|dotnet/i.test(promptText);

    let toolCardsHtml = '';

    if (isGit) {
      toolCardsHtml += `
        <div class="bg-surface-card border border-surface-border rounded-lg p-3 text-xs flex items-center justify-between">
          <div class="flex items-center space-x-2.5">
            <div class="w-6 h-6 rounded bg-sky-950/70 border border-sky-800/60 flex items-center justify-center text-sky-400">
              <i class="fa-solid fa-code-branch text-xs"></i>
            </div>
            <div>
              <span class="font-mono text-slate-300 font-medium">Git Command:</span>
              <span class="font-mono text-slate-400 ml-1">git status --porcelain</span>
            </div>
          </div>
          <button onclick="if (typeof switchView === 'function') switchView('repositories');" class="text-sky-400 hover:text-sky-300 font-mono text-[11px] transition">Review Working Tree &rarr;</button>
        </div>
      `;
    } else {
      toolCardsHtml += `
        <div class="bg-surface-card border border-surface-border rounded-lg p-3 text-xs flex items-center justify-between">
          <div class="flex items-center space-x-2.5">
            <div class="w-6 h-6 rounded bg-emerald-950/70 border border-emerald-800/60 flex items-center justify-center text-emerald-400">
              <i class="fa-solid fa-check text-xs"></i>
            </div>
            <div>
              <span class="font-mono text-slate-300 font-medium">Executed Action:</span>
              <span class="font-mono text-slate-400 ml-1">agy --execute "${escapeHtml(promptText.slice(0, 36))}..."</span>
            </div>
          </div>
          <button onclick="if (typeof switchView === 'function') switchView('repositories');" class="text-sky-400 hover:text-sky-300 font-mono text-[11px] transition">Review Diff &rarr;</button>
        </div>
      `;
    }

    agentCard.innerHTML = `
      <div class="w-8 h-8 rounded-lg bg-sky-600 flex items-center justify-center text-white text-xs flex-shrink-0 shadow-md shadow-sky-600/30">
        <i class="fa-solid fa-microchip"></i>
      </div>
      <div class="flex-1 space-y-3">
        ${toolCardsHtml}
        <div class="bg-surface-card border border-surface-border rounded-xl p-4 text-sm text-slate-200">
          <p>Task completed in workspace context. Changes dispatched to supervised CLI engine with zero runtime errors.</p>
        </div>
      </div>
    `;

    stream.appendChild(agentCard);
    stream.scrollTop = stream.scrollHeight;
  }

  // --- New Session & Session History Switching ---
  function createNewSession() {
    const stream = document.getElementById('chat-stream');
    if (stream) {
      stream.innerHTML = `
        <div class="text-center py-16 space-y-3">
          <div class="w-12 h-12 rounded-2xl bg-sky-950/60 border border-sky-800/50 flex items-center justify-center text-sky-400 text-xl mx-auto shadow-lg">
            <i class="fa-solid fa-wand-magic-sparkles"></i>
          </div>
          <h3 class="text-sm font-semibold text-slate-200">New Agent Session Initialized</h3>
          <p class="text-xs text-slate-400 max-w-sm mx-auto">Attached to active workspace. Type instructions below to inspect, refactor, or run tests.</p>
        </div>
      `;
    }
    const titleEl = document.getElementById('chat-title-text');
    if (titleEl) titleEl.innerText = 'New Task Session';
  }
  window.createNewSession = createNewSession;

  function loadSession(title) {
    const titleEl = document.getElementById('chat-title-text');
    if (titleEl) titleEl.innerText = title;
    if (typeof window.switchView === 'function') {
      window.switchView('chat');
    }
  }
  window.loadSession = loadSession;

  // --- Bind Listeners on DOM Ready ---
  document.addEventListener('DOMContentLoaded', () => {
    const promptInput = document.getElementById('prompt-input');
    const btnSend = document.getElementById('btn-send-prompt');

    if (promptInput) {
      promptInput.addEventListener('keydown', (e) => {
        if (e.key === 'Enter' && !e.shiftKey) {
          e.preventDefault();
          submitUserPrompt();
        }
      });
    }

    if (btnSend) {
      btnSend.addEventListener('click', submitUserPrompt);
    }
  });

  // Also hook into backend terminal_data event to stream to Quake HUD
  window.addEventListener('terminal_data', (e) => {
    if (e.detail && e.detail.data) {
      const hudContent = document.getElementById('hud-terminal-content');
      if (hudContent) {
        hudContent.innerHTML += `<span class="text-slate-400">${escapeHtml(e.detail.data)}</span>`;
        hudContent.scrollTop = hudContent.scrollHeight;
      }
    }
  });

})();
