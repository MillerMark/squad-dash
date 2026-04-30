/**
 * AudioWorkletProcessor — Captures PCM from the microphone at native sample rate,
 * resamples to 16 kHz / 16-bit signed little-endian mono, and sends ArrayBuffers
 * to the main thread for forwarding over WebSocket as binary frames.
 *
 * Azure Cognitive Services Speech SDK expects: 16 kHz, 16-bit, mono, PCM LE.
 */
class PcmCaptureProcessor extends AudioWorkletProcessor {
  constructor(options) {
    super();
    this._targetRate = 16000;
    this._nativeSampleRate = sampleRate; // global provided by AudioWorkletGlobalScope
    this._ratio = this._nativeSampleRate / this._targetRate; // e.g. 3 for 48kHz
    this._buffer = new Float32Array(0);
  }

  process(inputs) {
    const input = inputs[0];
    if (!input || !input[0]) return true;

    // Mix to mono (take channel 0 only)
    const mono = input[0];

    // Append to accumulation buffer
    const combined = new Float32Array(this._buffer.length + mono.length);
    combined.set(this._buffer);
    combined.set(mono, this._buffer.length);
    this._buffer = combined;

    // Resample: nearest-neighbour downsampling to 16 kHz
    const outputLength = Math.floor(this._buffer.length / this._ratio);
    if (outputLength === 0) return true;

    const int16 = new Int16Array(outputLength);
    for (let i = 0; i < outputLength; i++) {
      const srcIdx = Math.floor(i * this._ratio);
      // Clamp float [-1, 1] to int16 range
      const clamped = Math.max(-1, Math.min(1, this._buffer[srcIdx]));
      int16[i] = clamped < 0 ? clamped * 0x8000 : clamped * 0x7FFF;
    }

    // Keep unconsumed samples
    const consumed = Math.floor(outputLength * this._ratio);
    this._buffer = this._buffer.slice(consumed);

    // Transfer the Int16Array buffer to main thread (zero-copy)
    this.port.postMessage(int16.buffer, [int16.buffer]);

    return true; // keep processor alive
  }
}

registerProcessor('pcm-capture-processor', PcmCaptureProcessor);
