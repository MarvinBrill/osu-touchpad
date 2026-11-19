package dev.osutouchpad;

import java.io.IOException;
import java.net.Socket;
import java.nio.ByteBuffer;
import java.security.*;
import java.security.cert.CertificateException;
import java.security.cert.X509Certificate;
import java.util.Locale;
import javax.net.ssl.*;

final class Pairing {
    static final SecureRandom RANDOM = new SecureRandom();

    static SSLSocket tls(Socket raw, String expectedPin) throws GeneralSecurityException, IOException {
        X509TrustManager verifier = new X509TrustManager() {
            public void checkClientTrusted(X509Certificate[] chain, String auth) throws CertificateException {
                throw new CertificateException();
            }
            public void checkServerTrusted(X509Certificate[] chain, String auth) throws CertificateException {
                try {
                    if (chain.length == 0 || !expectedPin.equals(hex(sha256(chain[0].getEncoded()))))
                        throw new CertificateException("PC-Fingerabdruck stimmt nicht überein");
                } catch (NoSuchAlgorithmException e) { throw new CertificateException(e); }
            }
            public X509Certificate[] getAcceptedIssuers() { return new X509Certificate[0]; }
        };
        SSLContext context = SSLContext.getInstance("TLS");
        context.init(null, new TrustManager[]{verifier}, RANDOM);
        SSLSocket socket = (SSLSocket)context.getSocketFactory().createSocket(raw,
                raw.getInetAddress().getHostAddress(), raw.getPort(), true);
        socket.setSoTimeout(3500);
        socket.startHandshake();
        return socket;
    }

    static String code(SSLSocket socket, byte[] clientNonce, byte[] serverNonce) throws GeneralSecurityException, IOException {
        byte[] pin = sha256(socket.getSession().getPeerCertificates()[0].getEncoded());
        byte[] digest = sha256(ByteBuffer.allocate(64).put(pin).put(clientNonce).put(serverNonce).array());
        long value = Integer.toUnsignedLong(ByteBuffer.wrap(digest).getInt());
        return String.format(Locale.ROOT, "%06d", value % 1_000_000);
    }

    private static byte[] sha256(byte[] data) throws NoSuchAlgorithmException {
        return MessageDigest.getInstance("SHA-256").digest(data);
    }

    private static String hex(byte[] data) {
        StringBuilder result = new StringBuilder(data.length * 2);
        for (byte value : data) result.append(String.format(Locale.ROOT, "%02x", value));
        return result.toString();
    }

    private Pairing() {}
}
