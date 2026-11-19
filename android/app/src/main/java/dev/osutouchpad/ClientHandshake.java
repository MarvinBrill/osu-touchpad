package dev.osutouchpad;

import java.io.*;
import java.net.Socket;
import java.security.*;
import java.util.function.Consumer;
import javax.net.ssl.SSLSocket;

final class ClientHandshake {
    record Welcome(byte[] sessionKey, byte[] credential) {}

    static Welcome open(Socket socket, DataInputStream in, DataOutputStream out, int width, int height,
                        String id, String model, byte[] credential, Consumer<String> showCode)
            throws IOException, GeneralSecurityException {
        boolean lan = socket instanceof SSLSocket;
        byte[] nonce = new byte[16];
        Pairing.RANDOM.nextBytes(nonce);
        // Commit before the PC chooses its challenge, then reveal for the code comparison.
        Wire.hello(out, lan ? Wire.WIFI : Wire.USB, width, height, id, model, credential,
                MessageDigest.getInstance("SHA-256").digest(nonce));
        int reply = in.readUnsignedByte();
        if (lan && reply == Wire.PAIR) {
            byte[] challenge = new byte[16];
            in.readFully(challenge);
            out.write(nonce);
            out.flush();
            showCode.accept(Pairing.code((SSLSocket)socket, nonce, challenge));
            socket.setSoTimeout(48_000);
            reply = in.readUnsignedByte();
        }
        if (reply != Wire.WELCOME) throw new IOException("Sitzung abgelehnt");
        byte[] key = new byte[Wire.KEY_SIZE], saved = new byte[Wire.KEY_SIZE];
        in.readFully(key);
        in.readFully(saved);
        return new Welcome(key, saved);
    }

    private ClientHandshake() {}
}
