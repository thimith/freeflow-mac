package com.freeflow.bubble;

import android.app.Notification;
import android.app.NotificationChannel;
import android.app.NotificationManager;
import android.app.Service;
import android.content.Context;
import android.content.Intent;
import android.content.pm.ServiceInfo;
import android.os.IBinder;

/**
 * Foreground service of type "microphone". Android only lets an app use the mic while it's
 * in the foreground or running one of these — so every recording happens inside it.
 */
public class RecorderService extends Service {
    static final String CHANNEL = "recording";

    @Override public IBinder onBind(Intent i) { return null; }

    @Override
    public int onStartCommand(Intent intent, int flags, int startId) {
        ensureChannel(this);
        Notification n = new Notification.Builder(this, CHANNEL)
                .setSmallIcon(android.R.drawable.ic_btn_speak_now)
                .setContentTitle("FreeFlow is listening")
                .setContentText("Tap the bubble again to finish")
                .setOngoing(true)
                .build();
        try {
            startForeground(42, n, ServiceInfo.FOREGROUND_SERVICE_TYPE_MICROPHONE);
        } catch (Exception e) {
            // Android refused the mic (app considered "in background"). We still must call startForeground
            // once or Android crashes the app, so satisfy it with a short-service, then bail out.
            try {
                if (android.os.Build.VERSION.SDK_INT >= 34)
                    startForeground(42, n, ServiceInfo.FOREGROUND_SERVICE_TYPE_SHORT_SERVICE);
            } catch (Exception ignored) { }
            FlowService s = FlowService.instance;
            if (s != null) s.onMicBlocked();
            stopForeground(STOP_FOREGROUND_REMOVE);
            stopSelf();
            return START_NOT_STICKY;
        }
        Mic.start();
        return START_NOT_STICKY;
    }

    @Override
    public void onDestroy() {
        if (Mic.isRunning()) Mic.cancel();
        super.onDestroy();
    }

    static void ensureChannel(Context c) {
        NotificationManager nm = c.getSystemService(NotificationManager.class);
        if (nm.getNotificationChannel(CHANNEL) == null) {
            NotificationChannel ch = new NotificationChannel(CHANNEL, "Recording", NotificationManager.IMPORTANCE_LOW);
            ch.setDescription("Shown while FreeFlow is listening");
            ch.setShowBadge(false);
            nm.createNotificationChannel(ch);
        }
    }

    static void stop(Context c) {
        c.stopService(new Intent(c, RecorderService.class));
    }
}
