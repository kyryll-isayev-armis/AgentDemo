let ctx = null;

function getContext() {
    if (ctx) return ctx;
    const Ctor = window.AudioContext || window.webkitAudioContext;
    if (!Ctor) return null;
    ctx = new Ctor();
    return ctx;
}

// Each cue is a list of [startOffsetSeconds, frequencyHz, durationSeconds, waveType, endFrequencyHz?]
const cues = {
    click: [[0, 520, 0.05, "square"]],
    placeX: [[0, 660, 0.07, "triangle"], [0.05, 880, 0.08, "triangle"]],
    placeO: [[0, 440, 0.07, "sine"], [0.05, 330, 0.09, "sine"]],
    win: [[0, 523, 0.12, "triangle"], [0.11, 659, 0.12, "triangle"], [0.22, 784, 0.2, "triangle"]],
    lose: [[0, 330, 0.14, "sawtooth"], [0.13, 262, 0.14, "sawtooth"], [0.26, 196, 0.22, "sawtooth"]],
    draw: [[0, 392, 0.12, "sine"], [0.12, 392, 0.14, "sine"]],
    fade: [[0, 300, 0.1, "sine", 150]]
};

const VOLUME = 0.06;

export function play(name) {
    try {
        const notes = cues[name];
        const c = getContext();
        if (!notes || !c) return false;
        if (c.state === "suspended") c.resume().catch(() => { });

        const now = c.currentTime;
        for (const [offset, freq, dur, type, endFreq] of notes) {
            const osc = c.createOscillator();
            const gain = c.createGain();
            const t0 = now + offset;
            osc.type = type;
            osc.frequency.setValueAtTime(freq, t0);
            if (endFreq) osc.frequency.exponentialRampToValueAtTime(endFreq, t0 + dur);
            gain.gain.setValueAtTime(0.0001, t0);
            gain.gain.exponentialRampToValueAtTime(VOLUME, t0 + 0.01);
            gain.gain.exponentialRampToValueAtTime(0.0001, t0 + dur);
            osc.connect(gain).connect(c.destination);
            osc.start(t0);
            osc.stop(t0 + dur + 0.02);
        }
        return true;
    } catch {
        return false;
    }
}

// Browsers require a user gesture before audio may start.
function unlock() {
    try {
        const c = getContext();
        if (c && c.state === "suspended") c.resume().catch(() => { });
    } catch { }
}

["pointerdown", "keydown", "touchstart"].forEach(e =>
    window.addEventListener(e, unlock, { passive: true }));
