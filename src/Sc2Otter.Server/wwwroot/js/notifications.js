// Sc2Otter notification sounds
(function () {
    let audioContext = null;

    function getAudioContext() {
        if (!audioContext) {
            audioContext = new (window.AudioContext || window.webkitAudioContext)();
        }
        return audioContext;
    }

    // Play a pleasant chime when an opponent is detected
    window.playOpponentDetectedSound = function () {
        try {
            const ctx = getAudioContext();
            const now = ctx.currentTime;

            // Two-note chime (C5 -> E5)
            [523.25, 659.25].forEach((freq, i) => {
                const osc = ctx.createOscillator();
                const gain = ctx.createGain();
                osc.type = 'sine';
                osc.frequency.value = freq;
                gain.gain.setValueAtTime(0, now + i * 0.15);
                gain.gain.linearRampToValueAtTime(0.3, now + i * 0.15 + 0.05);
                gain.gain.exponentialRampToValueAtTime(0.001, now + i * 0.15 + 0.5);
                osc.connect(gain);
                gain.connect(ctx.destination);
                osc.start(now + i * 0.15);
                osc.stop(now + i * 0.15 + 0.5);
            });
        } catch (e) {
            console.log('Audio notification failed:', e);
        }
    };

    // Focus the note input textarea
    window.focusNoteInput = function () {
        const textarea = document.querySelector('.note-editor textarea');
        if (textarea) {
            textarea.focus();
            textarea.scrollIntoView({ behavior: 'smooth', block: 'center' });
        }
    };
})();
