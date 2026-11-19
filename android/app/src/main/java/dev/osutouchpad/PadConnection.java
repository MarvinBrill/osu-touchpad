package dev.osutouchpad;

import android.content.*;
import android.net.ConnectivityManager;
import android.os.BatteryManager;
import android.os.Build;
import android.util.Base64;
import java.io.*;
import java.net.*;
import java.security.GeneralSecurityException;
import java.util.UUID;

final class PadConnection extends Thread {
    interface Listener extends PadSession.ConfigListener { void status(String message); }
    private final Context context;
    private final Listener listener;
    private final SharedPreferences trust;
    private final int width, height;
    private final String id;
    private volatile boolean stopped;
    private volatile Socket current;

    PadConnection(Context context, int width, int height, Listener listener) {
        super("touchpad-connection");
        this.context = context.getApplicationContext();
        this.width = width;
        this.height = height;
        this.listener = listener;
        trust = context.getSharedPreferences("pairing", Context.MODE_PRIVATE);
        id = trust.getString("id", UUID.randomUUID().toString().replace("-", ""));
        trust.edit().putString("id", id).apply();
    }

    void shutdown() {
        stopped = true;
        interrupt();
        Socket open = current;
        if (open != null) try { open.close(); } catch (IOException ignored) {}
    }

    @Override public void run() {
        ConnectivityManager manager = context.getSystemService(ConnectivityManager.class);
        while (!stopped) {
            if (wired()) {
                listener.status("USB-Empfänger wird gesucht …");
                try {
                    connect(null);
                    continue;
                } catch (IOException | GeneralSecurityException unavailable) { /* try LAN in the same pass */ }
            }
            if (stopped) return;
            listener.status("PC im WLAN wird gesucht …");
            try {
                WifiDiscovery.Peer peer = WifiDiscovery.find(manager, trust);
                if (peer != null && !stopped) connect(peer);
            } catch (IOException | GeneralSecurityException unavailable) { /* retry after a short pause */ }
            if (stopped) return;
            try { Thread.sleep(550); } catch (InterruptedException done) { return; }
        }
    }

    private boolean wired() {
        Intent power = context.registerReceiver(null, new IntentFilter(Intent.ACTION_BATTERY_CHANGED));
        int plug = power == null ? 0 : power.getIntExtra(BatteryManager.EXTRA_PLUGGED, 0);
        Intent usb = context.registerReceiver(null, new IntentFilter("android.hardware.usb.action.USB_STATE"));
        return plug == BatteryManager.BATTERY_PLUGGED_USB || plug == BatteryManager.BATTERY_PLUGGED_AC
                || usb != null && usb.getBooleanExtra("connected", false);
    }

    private void connect(WifiDiscovery.Peer peer) throws IOException, GeneralSecurityException {
        boolean lan = peer != null;
        try (Socket raw = new Socket()) {
            current = raw;
            if (stopped) return;
            if (lan) peer.network().bindSocket(raw);
            InetAddress address = lan ? peer.address() : InetAddress.getByAddress(new byte[]{127, 0, 0, 1});
            raw.connect(new InetSocketAddress(address, lan ? Wire.LAN_PORT : Wire.USB_PORT), lan ? 1200 : 350);
            raw.setTcpNoDelay(true);
            raw.setSoTimeout(lan ? 3500 : 900);
            try (Socket socket = lan ? Pairing.tls(raw, peer.pin()) : raw) {
                DataInputStream in = new DataInputStream(new BufferedInputStream(socket.getInputStream()));
                DataOutputStream out = new DataOutputStream(socket.getOutputStream());
                String token = lan ? trust.getString("pc." + peer.pin(), "") : "";
                var welcome = ClientHandshake.open(socket, in, out, width, height, id, Build.MODEL,
                        Base64.decode(token, Base64.NO_WRAP), code ->
                                listener.status("Code " + code + "  ·  Am PC vergleichen und bestätigen"));
                if (lan) trust.edit().putString("pc." + peer.pin(), Base64.encodeToString(welcome.credential(), Base64.NO_WRAP)).apply();
                try (DatagramSocket udp = lan ? new DatagramSocket() : null) {
                    if (udp != null) {
                        peer.network().bindSocket(udp);
                        udp.connect(address, Wire.LAN_PORT);
                    }
                    new PadSession(socket, udp, welcome.sessionKey()).run(in, out, listener);
                }
            }
        } finally { current = null; }
    }
}
