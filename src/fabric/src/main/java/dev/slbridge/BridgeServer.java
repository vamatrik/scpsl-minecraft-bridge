package dev.slbridge;

import com.google.gson.JsonObject;
import com.google.gson.JsonParser;

import java.io.BufferedReader;
import java.io.BufferedWriter;
import java.io.IOException;
import java.io.InputStreamReader;
import java.io.OutputStreamWriter;
import java.net.InetAddress;
import java.net.ServerSocket;
import java.net.Socket;
import java.nio.charset.StandardCharsets;
import java.util.function.Consumer;

/**
 * TCP-сервер на 127.0.0.1. К нему подключается плагин SCP:SL.
 * Протокол: по одному JSON-объекту на строку (см. docs/PROTOCOL.md).
 */
public final class BridgeServer {
    private final int port;
    private final Consumer<JsonObject> handler;
    private final Runnable onConnect;
    private final Runnable onDisconnect;
    private volatile Socket socket;
    private volatile BufferedWriter writer;

    public BridgeServer(int port, Consumer<JsonObject> handler, Runnable onConnect, Runnable onDisconnect) {
        this.port = port;
        this.handler = handler;
        this.onConnect = onConnect;
        this.onDisconnect = onDisconnect;
    }

    public void start() {
        Thread t = new Thread(this::acceptLoop, "SLBridge-Accept");
        t.setDaemon(true);
        t.start();
    }

    public boolean isConnected() { return writer != null; }

    private void acceptLoop() {
        try (ServerSocket server = new ServerSocket(port, 1, InetAddress.getLoopbackAddress())) {
            SlBridgeClient.LOG.info("[SLBridge] Жду подключения SCP:SL на 127.0.0.1:{}", port);
            while (true) {
                Socket s = server.accept();
                s.setTcpNoDelay(true);
                closeCurrent();
                socket = s;
                writer = new BufferedWriter(new OutputStreamWriter(s.getOutputStream(), StandardCharsets.UTF_8));
                SlBridgeClient.LOG.info("[SLBridge] SCP:SL подключился: {}", s.getRemoteSocketAddress());
                onConnect.run();
                readLoop(s);
            }
        } catch (IOException e) {
            SlBridgeClient.LOG.error("[SLBridge] Не удалось открыть порт {}", port, e);
        }
    }

    private void readLoop(Socket s) {
        try (BufferedReader r = new BufferedReader(new InputStreamReader(s.getInputStream(), StandardCharsets.UTF_8))) {
            String line;
            while ((line = r.readLine()) != null) {
                if (line.isBlank()) continue;
                try {
                    handler.accept(JsonParser.parseString(line).getAsJsonObject());
                } catch (Exception e) {
                    SlBridgeClient.LOG.warn("[SLBridge] Ошибка обработки сообщения: {}", line, e);
                }
            }
        } catch (IOException ignored) {
        } finally {
            if (socket == s) {
                closeCurrent();
                SlBridgeClient.LOG.info("[SLBridge] SCP:SL отключился");
                onDisconnect.run();
            }
        }
    }

    private void closeCurrent() {
        writer = null;
        Socket s = socket;
        socket = null;
        if (s != null) try { s.close(); } catch (IOException ignored) {}
    }

    public void send(JsonObject msg) {
        BufferedWriter w = writer;
        if (w == null) return;
        synchronized (this) {
            try {
                w.write(msg.toString());
                w.write('\n');
                w.flush();
            } catch (IOException e) {
                closeCurrent();
            }
        }
    }
}
