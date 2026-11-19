package dev.osutouchpad;

final class TouchArea {
    static final int UNITS = 10_000;
    static final TouchArea DEFAULT = new TouchArea(1000, 1000, 8000, 8000);
    final int x, y, width, height;

    TouchArea(int x, int y, int width, int height) {
        this.x = x;
        this.y = y;
        this.width = width;
        this.height = height;
    }

    boolean valid() {
        return x >= 0 && y >= 0 && width > 0 && height > 0 && x + width <= UNITS && y + height <= UNITS;
    }

    boolean contains(int px, int py) {
        return px >= x && px <= x + width && py >= y && py <= y + height;
    }

    static int coordinate(float pixel, int extent) {
        return Math.round(Math.max(0f, Math.min(1f, pixel / Math.max(1, extent - 1))) * UNITS);
    }
}
