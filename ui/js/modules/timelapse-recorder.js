  function setupTimelapseRecorder() {
    const btnRec = document.getElementById('btn-timelapse-record');
    const labelRec = document.getElementById('timelapse-btn-label');
    let isRecording = false;
    let frames = [];
    let captureInterval = null;

    btnRec?.addEventListener('click', () => {
      if (isRecording) {
        stopRecording();
      } else {
        startRecording();
      }
    });

    function startRecording() {
      isRecording = true;
      frames = [];
      btnRec?.classList.add('btn-recording-active');
      if (labelRec) labelRec.textContent = 'Stop (0s)';

      const startTime = Date.now();
      const termHost = document.getElementById('terminal-host');

      captureInterval = setInterval(() => {
        const elapsed = Math.round((Date.now() - startTime) / 1000);
        if (labelRec) labelRec.textContent = `Stop (${elapsed}s)`;

        if (termHost) {
          const txt = termHost.innerText || '';
          frames.push({ t: elapsed, snapshot: txt.substring(0, 1500) });
        }

        if (frames.length > 120) {
          stopRecording();
        }
      }, 1000);

      showToast('Timelapse recording started...', 'info');
    }

    async function stopRecording() {
      isRecording = false;
      if (captureInterval) {
        clearInterval(captureInterval);
        captureInterval = null;
      }

      btnRec?.classList.remove('btn-recording-active');
      if (labelRec) labelRec.textContent = 'Record Clip';

      if (frames.length === 0) return;

      showToast('Encoding timelapse clip...', 'info');
      try {
        const jsonStr = JSON.stringify({
          recordedAt: new Date().toISOString(),
          durationSec: frames.length,
          frames
        });
        const base64Data = btoa(unescape(encodeURIComponent(jsonStr)));
        const wsId = state.activeWorkspace?.id || null;
        const res = await callBridge('save_timelapse_clip', {
          base64Data,
          workspaceId: wsId,
          clipName: `timelapse_${Date.now()}`
        });

        if (res?.success) {
          showToast(`Timelapse clip saved to: ${res.filePath || '.agents/timelapse/'}`, 'success');
        }
      } catch (err) {
        showToast(`Timelapse save error: ${err.message}`, 'error');
      }
    }
  }

  window.setupTimelapseRecorder = setupTimelapseRecorder;
