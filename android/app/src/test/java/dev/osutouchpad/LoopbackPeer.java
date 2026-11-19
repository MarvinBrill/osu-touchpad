package dev.osutouchpad;

import java.io.*;
import java.net.*;
import java.nio.charset.StandardCharsets;
import java.nio.file.*;

/** Desktop JVM peer used by the Windows integration checks. Commands stand in for MotionEvent. */
public final class LoopbackPeer {
    private final PadSession session;
    private final TouchTracker fingers;
    private int revision;
    private TouchArea area;

    private LoopbackPeer(PadSession session) {
        this.session = session;
        fingers = new TouchTracker((kind, x, y) -> session.send(kind, revision, x, y));
    }

    private synchronized void config(PadSession peer, int rev, TouchArea next) {
        fingers.configure(next);
        revision = rev;
        area = next;
        System.out.println("AREA " + next.x + " " + next.y + " " + next.width + " " + next.height);
    }

    private synchronized void gesture(String command) {
        switch (command) {
            case "aim" -> fingers.down(7, area.x, area.y);
            case "tap" -> { fingers.down(2, area.x, area.y); fingers.up(2); }
            case "move" -> { fingers.sample(10000, 10000); fingers.move(); }
            case "hold" -> fingers.down(2, area.x, area.y);
            default -> throw new IllegalArgumentException(command);
        }
    }

    // Simulate a half-open connection with no FIN or heartbeat reaching Windows.
    private static final class Link extends OutputStream {
        final OutputStream wire;
        volatile boolean silent;
        Link(OutputStream wire) { this.wire = wire; }
        @Override public void write(int b) throws IOException { if (!silent) wire.write(b); }
        @Override public void write(byte[] b, int off, int len) throws IOException { if (!silent) wire.write(b, off, len); }
        @Override public void flush() throws IOException { if (!silent) wire.flush(); }
    }

    public static void main(String[] args) throws Exception {
        InetAddress local = InetAddress.getByName("127.0.0.1");
        boolean lan = args[0].equals("wifi");
        Path trust = Paths.get(args[4]);
        try (Socket raw = new Socket(local, Integer.parseInt(args[1]));
             DatagramSocket udp = new DatagramSocket()) {
            raw.setTcpNoDelay(true);
            raw.setSoTimeout(4000);
            String pin = lan ? discover(udp, local, Integer.parseInt(args[2])) : "";
            try (Socket socket = lan ? Pairing.tls(raw, pin) : raw) {
                var in = new DataInputStream(new BufferedInputStream(socket.getInputStream()));
                var link = new Link(socket.getOutputStream());
                var out = new DataOutputStream(link);
                byte[] credential = lan && Files.exists(trust) ? Files.readAllBytes(trust) : new byte[0];
                var welcome = ClientHandshake.open(socket, in, out, 2340, 1080, "8194333980ed4846853553a220483997",
                        "Loopback", credential, code -> System.out.println("PAIR " + code));
                if (lan) {
                    Files.write(trust, welcome.credential());
                    udp.connect(local, Integer.parseInt(args[3]));
                }
                var session = new PadSession(socket, lan ? udp : null, welcome.sessionKey());
                var peer = new LoopbackPeer(session);
                Thread reader = new Thread(() -> {
                    try { session.run(in, out, peer::config); } catch (IOException disconnected) {}
                });
                reader.start();
                var commands = new BufferedReader(new InputStreamReader(System.in));
                for (String line; (line = commands.readLine()) != null && !line.equals("quit"); ) {
                    if (line.equals("silent")) link.silent = true;
                    else peer.gesture(line);
                }
                socket.close();
                reader.join(4000);
            }
        }
    }

    private static String discover(DatagramSocket udp, InetAddress dest, int port) throws IOException {
        byte[] query = Wire.DISCOVER.getBytes(StandardCharsets.US_ASCII);
        udp.send(new DatagramPacket(query, query.length, dest, port));
        var reply = new DatagramPacket(new byte[150], 150);
        udp.setSoTimeout(3000);
        udp.receive(reply);
        String advert = new String(reply.getData(), 0, reply.getLength(), StandardCharsets.US_ASCII);
        if (!advert.startsWith(Wire.ADVERT)) throw new IOException("Discovery reply");
        return advert.substring(Wire.ADVERT.length());
    }
}
