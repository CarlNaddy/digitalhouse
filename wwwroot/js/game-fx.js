// Game-HUD sound effects. Every sound is synthesized with the Web Audio API —
// no audio assets to license or ship — and gated behind the mute toggle
// (persisted in localStorage) plus a real user gesture, since browsers block
// AudioContext until one occurs anyway. attach() wires one delegated click
// listener so buttons/nav links get a blip for free without each component
// wiring its own handler.
window.gameFx = (() => {
    const STORAGE_KEY = "digitalhouse.sfx.muted";
    let ctx = null;
    let attached = false;

    function isMuted() {
        try {
            return localStorage.getItem(STORAGE_KEY) === "1";
        } catch {
            return false;
        }
    }

    function setMuted(muted) {
        try {
            localStorage.setItem(STORAGE_KEY, muted ? "1" : "0");
        } catch {
            // storage unavailable (private mode) — mute state just won't persist
        }
    }

    function audioContext() {
        if (isMuted()) {
            return null;
        }
        if (!ctx) {
            const AudioCtx = window.AudioContext || window.webkitAudioContext;
            if (!AudioCtx) {
                return null;
            }
            ctx = new AudioCtx();
        }
        if (ctx.state === "suspended") {
            ctx.resume();
        }
        return ctx;
    }

    // One short oscillator sweep. `notes` is a list of [freq, startOffsetSeconds].
    function blip(notes, { type = "square", duration = 0.09, gain = 0.05 } = {}) {
        const audio = audioContext();
        if (!audio) {
            return;
        }
        const now = audio.currentTime;
        const master = audio.createGain();
        master.gain.value = gain;
        master.connect(audio.destination);

        for (const [freq, offset = 0] of notes) {
            const osc = audio.createOscillator();
            const env = audio.createGain();
            osc.type = type;
            osc.frequency.setValueAtTime(freq, now + offset);
            env.gain.setValueAtTime(0.0001, now + offset);
            env.gain.exponentialRampToValueAtTime(1, now + offset + 0.008);
            env.gain.exponentialRampToValueAtTime(0.0001, now + offset + duration);
            osc.connect(env);
            env.connect(master);
            osc.start(now + offset);
            osc.stop(now + offset + duration + 0.02);
        }
    }

    return {
        isMuted,
        setMuted,
        playClick() {
            blip([[520, 0]], { type: "square", duration: 0.05, gain: 0.035 });
        },
        playSuccess() {
            blip([[523.25, 0], [659.25, 0.07], [783.99, 0.14]], { type: "triangle", duration: 0.12, gain: 0.05 });
        },
        playCoin() {
            blip([[987.77, 0], [1318.51, 0.06]], { type: "square", duration: 0.1, gain: 0.045 });
        },
        playError() {
            blip([[196, 0], [146.83, 0.09]], { type: "sawtooth", duration: 0.16, gain: 0.05 });
        },
        // Delegated so newly-rendered buttons/links pick up the sound with no
        // per-component wiring; call once (MainLayout does this on first render).
        attach() {
            if (attached) {
                return;
            }
            attached = true;
            document.addEventListener("click", (event) => {
                const target = event.target.closest(
                    ".mud-button-root:not(:disabled), .mud-nav-link, .mud-icon-button:not(:disabled)"
                );
                if (target) {
                    this.playClick();
                }
            });
        },
    };
})();
