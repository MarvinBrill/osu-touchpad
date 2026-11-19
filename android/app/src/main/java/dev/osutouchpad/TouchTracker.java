package dev.osutouchpad;

final class TouchTracker {
    interface Output { void send(int kind, int x, int y); }

    private final Output output;
    private TouchArea area = TouchArea.DEFAULT;
    private int aim = -1, modifier = -1;
    private int x, y;

    TouchTracker(Output output) { this.output = output; }

    void configure(TouchArea next) {
        cancel();
        area = next;
    }

    int aimId() { return aim; }

    void sample(int px, int py) {
        x = px;
        y = py;
    }

    void move() {
        if (aim >= 0) output.send(Wire.MOVE, x, y);
    }

    void down(int id, int px, int py) {
        if (!area.contains(px, py)) return;
        if (aim < 0) {
            aim = id;
            sample(px, py);
            output.send(Wire.START, x, y);
        } else if (modifier < 0 && id != aim) {
            modifier = id;
            output.send(Wire.DOWN, x, y);
        }
    }

    void up(int id) {
        if (id != aim && id != modifier) return;
        if (modifier >= 0) {
            modifier = -1;
            output.send(Wire.UP, x, y);
        }
        if (id == aim) {
            aim = -1;
            output.send(Wire.END, x, y);
        }
    }

    void cancel() {
        if (aim >= 0) output.send(Wire.CANCEL, x, y);
        aim = -1;
        modifier = -1;
    }
}
