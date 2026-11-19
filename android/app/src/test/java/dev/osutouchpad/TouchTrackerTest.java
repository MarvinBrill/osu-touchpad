package dev.osutouchpad;

import org.junit.Test;
import static org.junit.Assert.*;
import java.util.ArrayList;
import java.util.Arrays;
import java.util.List;

public class TouchTrackerTest {
    private final List<Integer> events = new ArrayList<>();
    private int lastX;
    private final TouchTracker pad = new TouchTracker((kind, x, y) -> { events.add(kind); lastX = x; });

    @Test public void outsideStartCannotEnterButInsideFingerCanAim() {
        pad.down(9, 0, 0);
        pad.move();
        pad.down(3, 1000, 9000);
        pad.sample(9999, 500);
        pad.move();
        assertEquals(9999, lastX); // the PC clips to its current target
        pad.up(9);
        pad.up(3);
        assertEquals(Arrays.asList(Wire.START, Wire.MOVE, Wire.END), events);
    }

    @Test public void quickTapsAndHoldHaveDistinctEdges() {
        pad.down(7, 3000, 4000);
        pad.down(2, 5000, 5000);
        pad.up(2);
        pad.down(4, 5000, 5000);
        pad.down(6, 5000, 5000);
        pad.sample(6000, 5000);
        pad.move();
        pad.up(6);
        pad.up(4);
        pad.move();
        assertEquals(7, pad.aimId());
        assertEquals(Arrays.asList(Wire.START, Wire.DOWN, Wire.UP, Wire.DOWN, Wire.MOVE, Wire.UP, Wire.MOVE), events);
    }

    @Test public void liftingAimDoesNotPromoteRemainingFinger() {
        pad.down(8, 2000, 2000);
        pad.down(2, 8000, 8000);
        pad.up(8);
        pad.move();
        pad.up(2);
        assertEquals(-1, pad.aimId());
        assertEquals(2000, lastX);
        assertEquals(Arrays.asList(Wire.START, Wire.DOWN, Wire.UP, Wire.END), events);
    }

    @Test public void cancelAndNewAreaInvalidateOldPointers() {
        pad.down(8, 4000, 4000);
        pad.down(3, 4000, 4000);
        pad.configure(new TouchArea(0, 0, 1000, 1000));
        pad.cancel();
        pad.move();
        pad.down(1, 4000, 4000);
        pad.down(2, 1000, 1000);
        assertEquals(Arrays.asList(Wire.START, Wire.DOWN, Wire.CANCEL, Wire.START), events);
    }

    @Test public void viewCoordinatesIncludeBothPhysicalEdges() {
        assertEquals(0, TouchArea.coordinate(-2, 2340));
        assertEquals(5000, TouchArea.coordinate(1169.5f, 2340));
        assertEquals(10000, TouchArea.coordinate(2339, 2340));
    }
}
