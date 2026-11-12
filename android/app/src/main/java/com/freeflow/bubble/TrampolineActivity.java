package com.freeflow.bubble;

import android.app.Activity;
import android.content.Intent;
import android.os.Bundle;

/**
 * Invisible, closes itself immediately. Opening it makes FreeFlow a "foreground" app for an
 * instant, which is what Android requires before it allows the microphone service to start.
 */
public class TrampolineActivity extends Activity {
    @Override
    protected void onCreate(Bundle b) {
        super.onCreate(b);
        try {
            startForegroundService(new Intent(this, RecorderService.class));
        } catch (Exception e) {
            FlowService s = FlowService.instance;
            if (s != null) s.onMicFailed("Android blocked the microphone");
        }
        finish();
        overridePendingTransition(0, 0);
    }
}
