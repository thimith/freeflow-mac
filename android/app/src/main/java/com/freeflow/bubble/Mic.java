package com.freeflow.bubble;

import android.annotation.SuppressLint;
import android.media.AudioFormat;
import android.media.AudioRecord;
import android.media.MediaRecorder;
import android.os.Handler;
import android.os.Looper;

import java.io.ByteArrayOutputStream;
import java.io.File;
import java.io.FileOutputStream;
import java.io.IOException;

/** 16 kHz mono PCM recorder → WAV. One recording at a time, process-wide. */
public final class Mic {
    public interface Listener {
        void onLevel(float level);
        void onStarted();
        void onFailed(String why);
    }

    private static final int RATE = 16000;
    private static AudioRecord rec;
    private static Thread thread;
    private static volatile boolean running;
    private static ByteArrayOutputStream pcm;
    private static volatile int maxAbs;
    private static volatile float peak;
    private static long startedAt;
    private static Listener listener;
    private static final Handler main = new Handler(Looper.getMainLooper());

    public static void setListener(Listener l) { listener = l; }
    public static boolean isRunning() { return running; }
    /** Largest absolute sample so far — 0 means Android is silencing the mic (app treated as background). */
    public static int maxAbs() { return maxAbs; }
    public static float peak() { return peak; }

    @SuppressLint("MissingPermission")
    public static synchronized void start() {
        if (running) return;
        int min = AudioRecord.getMinBufferSize(RATE, AudioFormat.CHANNEL_IN_MONO, AudioFormat.ENCODING_PCM_16BIT);
        try {
            rec = new AudioRecord(MediaRecorder.AudioSource.VOICE_RECOGNITION, RATE, AudioFormat.CHANNEL_IN_MONO,
                    AudioFormat.ENCODING_PCM_16BIT, Math.max(min, RATE));
            if (rec.getState() != AudioRecord.STATE_INITIALIZED) throw new IllegalStateException("mic unavailable");
            rec.startRecording();
        } catch (Exception e) {
            release();
            post(() -> { if (listener != null) listener.onFailed("Microphone unavailable: " + e.getMessage()); });
            return;
        }
        pcm = new ByteArrayOutputStream();
        maxAbs = 0;
        peak = 0;
        running = true;
        startedAt = System.currentTimeMillis();
        final AudioRecord r = rec;
        final ByteArrayOutputStream out = pcm;
        thread = new Thread(() -> {
            short[] buf = new short[RATE / 20];   // 50 ms
            byte[] bytes = new byte[buf.length * 2];
            while (running) {
                int n = r.read(buf, 0, buf.length);
                if (n <= 0) continue;
                double sum = 0;
                int localMax = 0;
                for (int i = 0; i < n; i++) {
                    short v = buf[i];
                    bytes[i * 2] = (byte) (v & 0xff);
                    bytes[i * 2 + 1] = (byte) ((v >> 8) & 0xff);
                    int a = Math.abs((int) v);
                    if (a > localMax) localMax = a;
                    double d = v / 32768.0;
                    sum += d * d;
                }
                synchronized (out) { out.write(bytes, 0, n * 2); }
                if (localMax > maxAbs) maxAbs = localMax;
                double db = 20 * Math.log10(Math.max(Math.sqrt(sum / n), 1e-6));
                float level = (float) Math.max(0, Math.min(1, (db + 50) / 45));
                if (level > peak) peak = level;
                post(() -> { if (listener != null) listener.onLevel(level); });
            }
        }, "FreeFlowMic");
        thread.start();
        post(() -> { if (listener != null) listener.onStarted(); });
    }

    /** Stops and writes a WAV. Returns null if nothing was recorded. */
    public static synchronized File stop(File dir) {
        if (!running) return null;
        running = false;
        try { if (thread != null) thread.join(400); } catch (InterruptedException ignored) { }
        release();
        byte[] data;
        synchronized (pcm) { data = pcm.toByteArray(); }
        pcm = null;
        if (data.length < RATE / 5) return null;   // < 0.1 s
        File f = new File(dir, "freeflow-" + System.currentTimeMillis() + ".wav");
        try (FileOutputStream o = new FileOutputStream(f)) {
            o.write(wavHeader(data.length));
            o.write(data);
        } catch (IOException e) {
            return null;
        }
        return f;
    }

    public static synchronized void cancel() {
        if (!running) return;
        running = false;
        try { if (thread != null) thread.join(400); } catch (InterruptedException ignored) { }
        release();
        pcm = null;
    }

    public static double seconds() { return running ? (System.currentTimeMillis() - startedAt) / 1000.0 : 0; }

    private static void release() {
        try { if (rec != null) { rec.stop(); } } catch (Exception ignored) { }
        try { if (rec != null) { rec.release(); } } catch (Exception ignored) { }
        rec = null;
    }

    private static void post(Runnable r) { main.post(r); }

    private static byte[] wavHeader(int dataLen) {
        int byteRate = RATE * 2;
        byte[] h = new byte[44];
        put(h, 0, "RIFF"); le32(h, 4, 36 + dataLen); put(h, 8, "WAVE"); put(h, 12, "fmt ");
        le32(h, 16, 16); le16(h, 20, 1); le16(h, 22, 1); le32(h, 24, RATE); le32(h, 28, byteRate);
        le16(h, 32, 2); le16(h, 34, 16); put(h, 36, "data"); le32(h, 40, dataLen);
        return h;
    }
    private static void put(byte[] b, int o, String s) { for (int i = 0; i < 4; i++) b[o + i] = (byte) s.charAt(i); }
    private static void le32(byte[] b, int o, int v) { b[o] = (byte) v; b[o + 1] = (byte) (v >> 8); b[o + 2] = (byte) (v >> 16); b[o + 3] = (byte) (v >> 24); }
    private static void le16(byte[] b, int o, int v) { b[o] = (byte) v; b[o + 1] = (byte) (v >> 8); }
}
