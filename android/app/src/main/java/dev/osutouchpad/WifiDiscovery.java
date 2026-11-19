package dev.osutouchpad;

import android.content.SharedPreferences;
import android.net.*;
import java.io.IOException;
import java.net.*;
import java.nio.charset.StandardCharsets;
import java.util.LinkedHashSet;
import java.util.Set;

final class WifiDiscovery {
    record Peer(Network network, InetAddress address, String pin) {}

    static Peer find(ConnectivityManager manager, SharedPreferences trust) throws IOException {
        for (Network network : manager.getAllNetworks()) {
            NetworkCapabilities caps = manager.getNetworkCapabilities(network);
            LinkProperties links = manager.getLinkProperties(network);
            if (caps == null || links == null || !caps.hasTransport(NetworkCapabilities.TRANSPORT_WIFI)) continue;
            Peer peer = discover(network, links, trust);
            if (peer != null) return peer;
        }
        return null;
    }

    private static Peer discover(Network network, LinkProperties links, SharedPreferences trust) throws IOException {
        Set<InetAddress> destinations = new LinkedHashSet<>();
        for (LinkAddress link : links.getLinkAddresses()) {
            if (!(link.getAddress() instanceof Inet4Address) || link.getPrefixLength() > 30) continue;
            byte[] bytes = link.getAddress().getAddress();
            for (int bit = link.getPrefixLength(); bit < 32; bit++) bytes[bit / 8] |= 1 << (7 - bit % 8);
            destinations.add(InetAddress.getByAddress(bytes));
        }
        if (destinations.isEmpty()) return null;
        destinations.add(InetAddress.getByName("255.255.255.255"));

        try (DatagramSocket socket = new DatagramSocket()) {
            network.bindSocket(socket);
            socket.setBroadcast(true);
            byte[] query = Wire.DISCOVER.getBytes(StandardCharsets.US_ASCII);
            boolean sent = false;
            for (InetAddress dest : destinations) {
                try {
                    socket.send(new DatagramPacket(query, query.length, dest, Wire.DISCOVERY_PORT));
                    sent = true;
                } catch (SocketException unreachable) { /* another broadcast route can still succeed */ }
            }
            if (!sent) return null;

            long deadline = System.nanoTime() + 700_000_000L;
            Peer first = null;
            byte[] bytes = new byte[150];
            while (System.nanoTime() < deadline) {
                socket.setSoTimeout((int)Math.max(1, (deadline - System.nanoTime()) / 1_000_000));
                DatagramPacket reply = new DatagramPacket(bytes, bytes.length);
                try { socket.receive(reply); } catch (SocketTimeoutException done) { break; }
                String message = new String(bytes, 0, reply.getLength(), StandardCharsets.US_ASCII);
                if (!message.startsWith(Wire.ADVERT)) continue;
                String pin = message.substring(Wire.ADVERT.length());
                if (!pin.matches("[0-9a-f]{64}")) continue;
                Peer candidate = new Peer(network, reply.getAddress(), pin);
                if (trust.contains("pc." + pin)) return candidate;
                if (first == null) first = candidate;
            }
            return first;
        }
    }

    private WifiDiscovery() {}
}
