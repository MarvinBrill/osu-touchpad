package dev.osutouchpad;

import java.io.*;
import java.net.*;
import java.nio.ByteBuffer;
import java.security.GeneralSecurityException;
import java.util.ArrayDeque;
import javax.crypto.Mac;
import javax.crypto.spec.SecretKeySpec;

final class PadSession {
    interface ConfigListener { void apply(PadSession session, int revision, TouchArea area); }

    private final Socket socket;
    private final DatagramSocket udp;
    private final byte[] key;
    private final ArrayDeque<byte[]> queue = new ArrayDeque<>();
    private int revision, phase;
    private long sequence;
    private boolean closed;

    PadSession(Socket socket, DatagramSocket udp, byte[] key) {
        this.socket = socket;
        this.udp = udp;
        this.key = key;
    }

    // The UI thread never performs network I/O. Only consecutive movements may merge.
    synchronized void send(int kind, int config, int x, int y) {
        if (closed) return;
        if (revision != config) {
            revision = config;
            phase = 0;
        }
        if (kind != Wire.MOVE) phase++;
        if (kind == Wire.MOVE && !queue.isEmpty() && queue.peekLast()[0] == Wire.MOVE) queue.removeLast();
        if (queue.size() >= 256) {
            closed = true;
        } else {
            queue.add(ByteBuffer.allocate(Wire.SAMPLE_SIZE).put((byte)kind).putInt(config)
                    .putInt(phase).putLong(++sequence).putShort((short)x).putShort((short)y).array());
        }
        notifyAll();
    }

    void run(DataInputStream in, DataOutputStream out, ConfigListener listener) throws IOException {
        socket.setSoTimeout(3500);
        Thread sender = new Thread(() -> writeLoop(out), "touchpad-input");
        sender.start();
        try {
            for (;;) {
                int kind = in.readUnsignedByte();
                if (kind == Wire.PONG) continue;
                if (kind != Wire.CONFIG) throw new IOException("Unbekannter Nachrichtentyp");
                int config = in.readInt();
                TouchArea area = new TouchArea(in.readUnsignedShort(), in.readUnsignedShort(),
                        in.readUnsignedShort(), in.readUnsignedShort());
                if (!area.valid()) throw new IOException("Ungültige Touchfläche");
                listener.apply(this, config, area);
            }
        } finally {
            synchronized (this) {
                closed = true;
                notifyAll();
            }
            socket.close();
            sender.interrupt();
        }
    }

    private void writeLoop(DataOutputStream out) {
        try {
            Mac mac = null;
            DatagramPacket packet = null;
            if (udp != null) {
                mac = Mac.getInstance("HmacSHA256");
                mac.init(new SecretKeySpec(key, "HmacSHA256"));
                packet = new DatagramPacket(new byte[Wire.SAMPLE_SIZE + Wire.TAG_SIZE], Wire.SAMPLE_SIZE + Wire.TAG_SIZE);
            }
            long nextPing = System.nanoTime();
            for (;;) {
                byte[] frame;
                synchronized (this) {
                    while (!closed && queue.isEmpty() && System.nanoTime() < nextPing)
                        wait(Math.max(1, (nextPing - System.nanoTime()) / 1_000_000));
                    if (closed) break;
                    frame = queue.poll();
                }
                if (System.nanoTime() >= nextPing) {
                    out.writeByte(Wire.PING);
                    out.flush();
                    nextPing = System.nanoTime() + 600_000_000L;
                }
                if (frame == null) continue;
                if (udp != null && frame[0] == Wire.MOVE) {
                    byte[] bytes = packet.getData();
                    System.arraycopy(frame, 0, bytes, 0, frame.length);
                    System.arraycopy(mac.doFinal(frame), 0, bytes, frame.length, Wire.TAG_SIZE);
                    udp.send(packet);
                } else {
                    out.write(frame);
                    out.flush();
                }
            }
        } catch (IOException | GeneralSecurityException | InterruptedException disconnected) {
            // Closing TCP wakes the reader and releases the PC-side gesture.
        } finally {
            try { socket.close(); } catch (IOException ignored) {}
        }
    }
}
