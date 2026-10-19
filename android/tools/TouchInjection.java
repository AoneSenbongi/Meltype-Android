// SPDX-License-Identifier: GPL-3.0-or-later
// Shell-only synthetic multi-touch driver; never included in the application.
import android.os.SystemClock;
import android.view.InputEvent;
import android.view.InputDevice;
import android.view.MotionEvent;
import java.lang.reflect.Method;

public class TouchInjection {
    public static void main(String[] args) throws Exception {
        Class<?> type = Class.forName("android.hardware.input.InputManagerGlobal");
        Object manager = type.getMethod("getInstance").invoke(null);
        Method inject = type.getMethod("injectInputEvent", InputEvent.class, int.class);
        MotionEvent.PointerProperties[] properties = new MotionEvent.PointerProperties[2];
        MotionEvent.PointerCoords[] coords = new MotionEvent.PointerCoords[2];
        for (int i = 0; i < 2; i++) {
            properties[i] = new MotionEvent.PointerProperties();
            properties[i].id = i; properties[i].toolType = MotionEvent.TOOL_TYPE_FINGER;
            coords[i] = new MotionEvent.PointerCoords();
            coords[i].x = Float.parseFloat(args[i * 2]);
            coords[i].y = Float.parseFloat(args[i * 2 + 1]);
            coords[i].pressure = 1; coords[i].size = 1;
        }
        long down = SystemClock.uptimeMillis();
        int[] actions = {MotionEvent.ACTION_DOWN,
            MotionEvent.ACTION_POINTER_DOWN | (1 << MotionEvent.ACTION_POINTER_INDEX_SHIFT),
            MotionEvent.ACTION_POINTER_UP | (1 << MotionEvent.ACTION_POINTER_INDEX_SHIFT),
            MotionEvent.ACTION_UP};
        int[] counts = {1, 2, 2, 1};
        for (int i = 0; i < actions.length; i++) {
            MotionEvent event = MotionEvent.obtain(down, SystemClock.uptimeMillis(), actions[i],
                counts[i], properties, coords, 0, 0, 1, 1, 0, 0, InputDevice.SOURCE_TOUCHSCREEN, 0);
            try {
                if (!Boolean.TRUE.equals(inject.invoke(manager, event, 2)))
                    throw new IllegalStateException("Touch injection failed");
            } finally { event.recycle(); }
            Thread.sleep(40);
        }
    }
}
