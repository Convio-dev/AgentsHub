  function setupVoiceCommander() {
    const btnMic = document.getElementById('btn-ptt-mic');
    const labelMic = document.getElementById('ptt-btn-label');
    let isListening = false;
    let recognition = null;

    const SpeechRec = window.SpeechRecognition || window.webkitSpeechRecognition;

    btnMic?.addEventListener('click', () => {
      if (isListening) {
        if (recognition) {
          try { recognition.stop(); } catch {}
        }
        stopListening();
        return;
      }

      if (!SpeechRec) {
        const text = prompt('Push-to-Talk (SpeechRecognition unavailable in this webview): Type command to inject:');
        if (text && text.trim()) {
          const activeCli = state.activeCliSession || 'claude_code';
          callBridge('inject_voice_command', { text: text.trim(), sessionName: activeCli });
          showToast(`Injected to ${activeCli}: "${text.trim()}"`, 'success');
        }
        return;
      }

      startListening();
    });

    function startListening() {
      try {
        recognition = new SpeechRec();
        recognition.continuous = false;
        recognition.interimResults = false;
        recognition.lang = navigator.language || 'en-US';

        recognition.onstart = () => {
          isListening = true;
          btnMic?.classList.add('btn-ptt-listening');
          if (labelMic) labelMic.textContent = 'Listening...';
        };

        recognition.onresult = (e) => {
          const text = e.results[0][0].transcript;
          if (text && text.trim()) {
            const activeCli = state.activeCliSession || 'claude_code';
            callBridge('inject_voice_command', { text: text.trim(), sessionName: activeCli });
            showToast(`Voice command injected to ${activeCli}: "${text.trim()}"`, 'success');
          }
          stopListening();
        };

        recognition.onerror = (e) => {
          console.warn('Voice recognition error:', e.error);
          stopListening();
        };

        recognition.onend = () => {
          stopListening();
        };

        recognition.start();
      } catch (err) {
        console.error('Failed to start voice recognition:', err);
        stopListening();
      }
    }

    function stopListening() {
      isListening = false;
      btnMic?.classList.remove('btn-ptt-listening');
      if (labelMic) labelMic.textContent = 'PTT Mic';
    }
  }

  // Feature 6: Pre-Commit AI Security Gatekeeper
window.setupVoiceCommander = setupVoiceCommander;
