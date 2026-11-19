package dev.osutouchpad;

import java.io.DataOutputStream;
import java.io.IOException;
import java.nio.charset.StandardCharsets;

final class Wire {
    static final int USB_PORT = 27183, LAN_PORT = 27184, DISCOVERY_PORT = 27185;
    static final int USB = 1, WIFI = 2;
    static final int MOVE = 1, DOWN = 2, UP = 3, CANCEL = 4, START = 5, END = 6, PING = 7;
    static final int PAIR = 16, WELCOME = 17, CONFIG = 18, PONG = 19;
    static final int SAMPLE_SIZE = 21, KEY_SIZE = 32, TAG_SIZE = 16;
    static final String DISCOVER = "osu-touchpad/1 discover", ADVERT = "osu-touchpad/1 pc ";

    static void hello(DataOutputStream out, int transport, int width, int height, String id,
                      String model, byte[] credential, byte[] nonce) throws IOException {
        out.writeBytes("OTP1");
        out.writeByte(transport);
        out.writeInt(width);
        out.writeInt(height);
        field(out, id.getBytes(StandardCharsets.US_ASCII));
        field(out, model.getBytes(StandardCharsets.UTF_8));
        field(out, credential);
        out.write(nonce);
        out.flush();
    }

    private static void field(DataOutputStream out, byte[] data) throws IOException {
        int length = Math.min(data.length, 255);
        out.writeByte(length);
        out.write(data, 0, length);
    }

    private Wire() {}
}
