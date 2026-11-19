package dev.osutouchpad;

import android.content.Context;
import android.graphics.Bitmap;
import android.graphics.BitmapFactory;
import android.graphics.Canvas;
import android.graphics.Color;
import android.graphics.Paint;
import android.graphics.RectF;
import android.graphics.Typeface;
import android.view.MotionEvent;
import android.view.View;

final class PadView extends View {
    private final Bitmap background;
    private final Paint paint = new Paint(Paint.ANTI_ALIAS_FLAG | Paint.FILTER_BITMAP_FLAG);
    private final RectF rect = new RectF();
    private final TouchTracker touches;
    private PadSession session;
    private int revision;
    private TouchArea area = TouchArea.DEFAULT;
    private String status = "PC wird gesucht …";

    PadView(Context context) {
        super(context);
        background = BitmapFactory.decodeResource(getResources(), R.drawable.playfield);
        paint.setTypeface(Typeface.create("sans-serif-medium", Typeface.NORMAL));
        touches = new TouchTracker((kind, x, y) -> {
            if (session != null) session.send(kind, revision, x, y);
        });
    }

    void connected(PadSession next, int nextRevision, TouchArea nextArea) {
        touches.configure(nextArea);
        session = next;
        revision = nextRevision;
        area = nextArea;
        status = "";
        invalidate();
    }

    void searching(String message) {
        touches.cancel();
        session = null;
        status = message;
        invalidate();
    }

    void cancelGesture() { touches.cancel(); }

    @Override protected void onDraw(Canvas canvas) {
        int w = getWidth(), h = getHeight();
        float scale = Math.max((float) w / background.getWidth(), (float) h / background.getHeight());
        float bw = scale * background.getWidth(), bh = scale * background.getHeight();
        rect.set((w - bw) / 2, (h - bh) / 2, (w + bw) / 2, (h + bh) / 2);
        paint.setColor(Color.WHITE);
        paint.setStyle(Paint.Style.FILL);
        canvas.drawBitmap(background, null, rect, paint);
        canvas.drawColor(0x65090713);

        float sx = (w - 1f) / TouchArea.UNITS, sy = (h - 1f) / TouchArea.UNITS;
        rect.set(area.x * sx, area.y * sy, (area.x + area.width) * sx, (area.y + area.height) * sy);
        paint.setColor(0xffff4958);
        paint.setStrokeWidth(dp(2));
        paint.setStyle(Paint.Style.STROKE);
        canvas.drawRect(rect, paint);
        if (status.isEmpty()) return;

        paint.setStyle(Paint.Style.FILL);
        paint.setTextSize(dp(14));
        paint.setTextAlign(Paint.Align.CENTER);
        float half = paint.measureText(status) / 2 + dp(16);
        float mid = h / 2f;
        rect.set(w / 2f - half, mid - dp(19), w / 2f + half, mid + dp(19));
        paint.setColor(0xe3242034);
        canvas.drawRoundRect(rect, dp(19), dp(19), paint);
        paint.setColor(0xfff7edf4);
        canvas.drawText(status, w / 2f, mid - (paint.ascent() + paint.descent()) / 2, paint);
    }

    private float dp(float value) { return value * getResources().getDisplayMetrics().density; }

    @Override public boolean onTouchEvent(MotionEvent event) {
        if (session == null) return true;
        int action = event.getActionMasked();
        if (action == MotionEvent.ACTION_CANCEL) {
            touches.cancel();
            return true;
        }
        int aimIndex = touches.aimId() < 0 ? -1 : event.findPointerIndex(touches.aimId());
        if (aimIndex >= 0) touches.sample(x(event, aimIndex), y(event, aimIndex));
        int index = event.getActionIndex();
        switch (action) {
            case MotionEvent.ACTION_DOWN:
                requestUnbufferedDispatch(event);
                // A new Android gesture is also a boundary after a lost CANCEL.
                touches.cancel();
                // fall through
            case MotionEvent.ACTION_POINTER_DOWN:
                touches.down(event.getPointerId(index), x(event, index), y(event, index));
                break;
            case MotionEvent.ACTION_MOVE:
                touches.move();
                break;
            case MotionEvent.ACTION_POINTER_UP:
            case MotionEvent.ACTION_UP:
                touches.up(event.getPointerId(index));
                break;
        }
        return true;
    }

    private int x(MotionEvent e, int i) { return TouchArea.coordinate(e.getX(i), getWidth()); }
    private int y(MotionEvent e, int i) { return TouchArea.coordinate(e.getY(i), getHeight()); }
}
