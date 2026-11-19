package dev.osutouchpad;

import android.app.Activity;
import android.os.Build;
import android.os.Bundle;
import android.view.*;

public final class PadActivity extends Activity {
    private PadView pad;
    private PadConnection connection;
    private boolean resumed;
    private int lifecycle;

    @Override public void onCreate(Bundle state) {
        super.onCreate(state);
        getWindow().addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON);
        if (Build.VERSION.SDK_INT >= 28) {
            WindowManager.LayoutParams layout = getWindow().getAttributes();
            layout.layoutInDisplayCutoutMode = WindowManager.LayoutParams.LAYOUT_IN_DISPLAY_CUTOUT_MODE_SHORT_EDGES;
            getWindow().setAttributes(layout);
        }
        pad = new PadView(this);
        pad.addOnLayoutChangeListener((v, l, t, r, b, ol, ot, or, ob) -> {
            if (resumed && (r - l != or - ol || b - t != ob - ot)) startConnection();
        });
        setContentView(pad);
        immersive();
    }

    @SuppressWarnings("deprecation")
    private void immersive() {
        if (Build.VERSION.SDK_INT >= 30) {
            getWindow().setDecorFitsSystemWindows(false);
            WindowInsetsController bars = getWindow().getInsetsController();
            if (bars != null) {
                bars.setSystemBarsBehavior(WindowInsetsController.BEHAVIOR_SHOW_TRANSIENT_BARS_BY_SWIPE);
                bars.hide(WindowInsets.Type.systemBars());
            }
            return;
        }
        getWindow().getDecorView().setSystemUiVisibility(View.SYSTEM_UI_FLAG_FULLSCREEN
                | View.SYSTEM_UI_FLAG_HIDE_NAVIGATION | View.SYSTEM_UI_FLAG_IMMERSIVE_STICKY
                | View.SYSTEM_UI_FLAG_LAYOUT_STABLE | View.SYSTEM_UI_FLAG_LAYOUT_FULLSCREEN
                | View.SYSTEM_UI_FLAG_LAYOUT_HIDE_NAVIGATION);
    }

    @Override public void onWindowFocusChanged(boolean focus) {
        super.onWindowFocusChanged(focus);
        if (focus) immersive();
        else if (pad != null) pad.cancelGesture();
    }

    @Override protected void onResume() {
        super.onResume();
        resumed = true;
        startConnection();
    }

    private void startConnection() {
        stopConnection();
        if (pad.getWidth() < 2 || pad.getHeight() < 2) return;
        int generation = lifecycle;
        PadConnection.Listener listener = new PadConnection.Listener() {
            @Override public void status(String message) {
                runOnUiThread(() -> { if (generation == lifecycle) pad.searching(message); });
            }
            @Override public void apply(PadSession session, int revision, TouchArea area) {
                runOnUiThread(() -> { if (generation == lifecycle) pad.connected(session, revision, area); });
            }
        };
        connection = new PadConnection(this, pad.getWidth(), pad.getHeight(), listener);
        connection.start();
    }

    private void stopConnection() {
        lifecycle++;
        pad.searching("PC wird gesucht …");
        if (connection != null) connection.shutdown();
        connection = null;
    }

    @Override protected void onPause() {
        resumed = false;
        stopConnection();
        super.onPause();
    }
}
