// Agents Hub - Onboarding Tour & Interactive User Guide Module
(function () {
  'use strict';

  let currentSlide = 0;
  const TOTAL_SLIDES = 5;

  function openHelpGuideModal(initialTab = 'tour') {
    const modal = document.getElementById('modal-help-guide');
    if (!modal) return;

    modal.classList.add('open', 'active');
    switchGuideTab(initialTab);
  }

  function closeHelpGuideModal() {
    const modal = document.getElementById('modal-help-guide');
    if (!modal) return;
    modal.classList.remove('open', 'active');
  }

  function switchGuideTab(tabKey) {
    const tabs = document.querySelectorAll('.guide-tab-btn');
    const panels = document.querySelectorAll('.guide-tab-panel');

    tabs.forEach(btn => {
      btn.classList.toggle('active', btn.dataset.guideTab === tabKey);
    });

    panels.forEach(panel => {
      panel.classList.toggle('active', panel.id === `guide-panel-${tabKey}`);
    });

    if (tabKey === 'tour') {
      goToSlide(currentSlide);
    }
  }

  function goToSlide(index) {
    if (index < 0) index = 0;
    if (index >= TOTAL_SLIDES) index = TOTAL_SLIDES - 1;
    currentSlide = index;

    const slides = document.querySelectorAll('.guide-slide');
    slides.forEach(s => {
      s.classList.toggle('active', parseInt(s.dataset.slide, 10) === currentSlide);
    });

    const dots = document.querySelectorAll('.guide-dot');
    dots.forEach(d => {
      d.classList.toggle('active', parseInt(d.dataset.dot, 10) === currentSlide);
    });

    const btnPrev = document.getElementById('btn-guide-prev');
    const btnNext = document.getElementById('btn-guide-next');

    if (btnPrev) btnPrev.disabled = currentSlide === 0;
    if (btnNext) {
      if (currentSlide === TOTAL_SLIDES - 1) {
        btnNext.style.display = 'none';
      } else {
        btnNext.style.display = 'inline-block';
        btnNext.textContent = 'Next →';
      }
    }
  }

  function setupOnboardingGuide() {
    // 1. Settings "About & Updates" Launchers & Header Button
    const bindGuideLauncher = (id) => {
      document.getElementById(id)?.addEventListener('click', (e) => {
        e.preventDefault();
        openHelpGuideModal('tour');
      });
    };
    bindGuideLauncher('btn-open-settings-guide');
    bindGuideLauncher('btn-about-link-guide');
    bindGuideLauncher('btn-open-help-guide');

    // 2. Modal Close Buttons
    const btnClose = document.getElementById('btn-close-help-guide');
    if (btnClose) {
      btnClose.addEventListener('click', closeHelpGuideModal);
    }

    const modal = document.getElementById('modal-help-guide');
    if (modal) {
      modal.addEventListener('click', (e) => {
        if (e.target === modal) closeHelpGuideModal();
      });
    }

    // 3. Tab Navigation
    document.querySelectorAll('.guide-tab-btn').forEach(btn => {
      btn.addEventListener('click', () => {
        const tab = btn.dataset.guideTab;
        if (tab) switchGuideTab(tab);
      });
    });

    // 4. Carousel Buttons
    document.getElementById('btn-guide-prev')?.addEventListener('click', () => {
      goToSlide(currentSlide - 1);
    });
    document.getElementById('btn-guide-next')?.addEventListener('click', () => {
      goToSlide(currentSlide + 1);
    });

    // Carousel Dots
    document.querySelectorAll('.guide-dot').forEach(dot => {
      dot.addEventListener('click', () => {
        const idx = parseInt(dot.dataset.dot, 10);
        if (!isNaN(idx)) goToSlide(idx);
      });
    });

    // 5. Interactive Action Buttons inside Tour
    const launchStandalone = () => {
      closeHelpGuideModal();
      if (typeof window.callBridge === 'function') {
        const activeCli = window.state?.activeTerminalCli || 'Antigravity';
        window.callBridge('open_standalone_terminals', { cli: activeCli }).catch(err => {
          console.error('Failed to open standalone terminals:', err);
        });
      }
    };

    document.getElementById('btn-guide-try-standalone')?.addEventListener('click', launchStandalone);
    document.getElementById('btn-guide-launch-standalone-direct')?.addEventListener('click', launchStandalone);

    document.getElementById('btn-guide-goto-terminals')?.addEventListener('click', () => {
      closeHelpGuideModal();
      if (typeof window.switchView === 'function') {
        window.switchView('terminals');
      }
    });

    // Finish Tour Button
    document.getElementById('btn-guide-finish')?.addEventListener('click', () => {
      const chk = document.getElementById('chk-guide-dont-show');
      if (chk && chk.checked) {
        localStorage.setItem('agents_hub_onboarding_completed', 'true');
      }
      closeHelpGuideModal();
      if (typeof window.showToast === 'function') {
        window.showToast('Welcome to AgentsHub! Press F1 anytime for help.', 'info');
      }
    });

    // 6. Global F1 & Escape Key Listeners
    window.addEventListener('keydown', (e) => {
      if (e.key === 'F1') {
        e.preventDefault();
        const m = document.getElementById('modal-help-guide');
        if (m && (m.classList.contains('open') || m.classList.contains('active'))) {
          closeHelpGuideModal();
        } else {
          openHelpGuideModal('tour');
        }
      } else if (e.key === 'Escape') {
        const m = document.getElementById('modal-help-guide');
        if (m && (m.classList.contains('open') || m.classList.contains('active'))) {
          closeHelpGuideModal();
        }
      }
    });

    // 7. Dashboard Quickstart Banner
    setupDashboardQuickstart();

    // 8. Auto-Open on First Launch
    try {
      const completed = localStorage.getItem('agents_hub_onboarding_completed');
      if (!completed) {
        setTimeout(() => {
          openHelpGuideModal('tour');
        }, 700);
      }
    } catch (e) {
      console.warn('Error reading onboarding status:', e);
    }
  }

  function setupDashboardQuickstart() {
    const banner = document.getElementById('dash-quickstart-banner');
    if (!banner) return;

    try {
      if (localStorage.getItem('agents_hub_quickstart_dismissed') === 'true') {
        banner.style.display = 'none';
        return;
      }
    } catch (e) {}

    // Dismiss Button
    document.getElementById('btn-dismiss-quickstart')?.addEventListener('click', () => {
      banner.style.display = 'none';
      try {
        localStorage.setItem('agents_hub_quickstart_dismissed', 'true');
      } catch (e) {}
    });

    // Launch Tour from Banner
    document.getElementById('btn-quickstart-launch-tour')?.addEventListener('click', () => {
      openHelpGuideModal('tour');
    });

    // Step 1: Workspace
    document.getElementById('btn-qs-open-workspace')?.addEventListener('click', () => {
      const btnWs = document.getElementById('btn-manage-workspaces') || document.querySelector('[data-action="manage-workspaces"]');
      if (btnWs) btnWs.click();
      else if (typeof window.openWorkspaceModal === 'function') window.openWorkspaceModal();
      else if (typeof window.switchView === 'function') window.switchView('repositories');
    });

    // Step 2: Profiles & Sign-in
    document.getElementById('btn-qs-open-profiles')?.addEventListener('click', () => {
      openHelpGuideModal('profiles');
    });

    // Step 3: DevTools
    document.getElementById('btn-qs-open-devtools')?.addEventListener('click', () => {
      if (typeof window.switchView === 'function') window.switchView('devtools');
    });

    // Step 4: Standalone Terminal
    document.getElementById('btn-qs-open-standalone')?.addEventListener('click', () => {
      if (typeof window.callBridge === 'function') {
        const activeCli = window.state?.activeTerminalCli || 'Antigravity';
        window.callBridge('open_standalone_terminals', { cli: activeCli }).catch(err => {
          console.error('Failed to open standalone terminals from banner:', err);
        });
      }
    });
  }

  // Export to window
  window.openHelpGuideModal = openHelpGuideModal;
  window.closeHelpGuideModal = closeHelpGuideModal;
  window.setupOnboardingGuide = setupOnboardingGuide;
})();
