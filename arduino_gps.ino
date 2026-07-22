#include <SoftwareSerial.h>
#include <TinyGPS++.h>
#include <R4HttpClient.h>
#include <PubSubClient.h>

// === CONFIGURAZIONE WIFI + MQTT ===
const char* ssid = "";
const char* password = "";
const char* mqtt_server = ""; // o il tuo broker locale
const int mqtt_port = 1883;

WiFiClient espClient;
PubSubClient client(espClient);

// === CONFIGURAZIONE GPS ===
SoftwareSerial gpsSerial(8, 9); // RX, TX
TinyGPSPlus gps;

// === VARIABILI ===
unsigned long previousMillis = 0;
const unsigned long interval = 3000; // ogni 3 secondi
char payload[128]; // buffer per JSON

// === Funzione per formattare due cifre ===
String twoDigits(int num) {
  if (num < 10) return "0" + String(num);
  return String(num);
}

// === Connetti al WiFi ===
void setup_wifi() {
  Serial.print("Connessione al WiFi ");
  Serial.println(ssid);

  WiFi.begin(ssid, password);

  while (WiFi.status() != WL_CONNECTED) {
    delay(500);
    Serial.print(".");
  }
  Serial.println("\nConnesso al WiFi!");
  Serial.print("IP: ");
  Serial.println(WiFi.localIP());
}

// === Riconnessione MQTT se cade ===
void reconnect() {
  while (!client.connected()) {
    Serial.print("Connessione MQTT...");
    if (client.connect("ArduinoGPS")) { // ID client
      Serial.println("connesso!");
      client.subscribe("gps/test"); // eventuale subscribe
    } else {
      Serial.print("fallita, rc=");
      Serial.print(client.state());
      Serial.println(" ritento tra 5s");
      delay(5000);
    }
  }
}

void setup() {
  Serial.begin(9600);
  gpsSerial.begin(9600);

  setup_wifi();
  client.setServer(mqtt_server, mqtt_port);
}

void loop() {
  // Mantieni connessione MQTT
  if (!client.connected()) {
    reconnect();
  }
  client.loop();

  // Leggi continuamente i dati dal GPS
  while (gpsSerial.available() > 0) {
    gps.encode(gpsSerial.read());
  }

  unsigned long currentMillis = millis();
  if (currentMillis - previousMillis >= interval) {
    previousMillis = currentMillis;

    if (gps.location.isValid() && gps.date.isValid() && gps.time.isValid()) {
      // Coordinate
      float lat = gps.location.lat();
      float lon = gps.location.lng();

      // Timestamp ISO 8601
      String timestamp = String(gps.date.year()) + "-" +
                         twoDigits(gps.date.month()) + "-" +
                         twoDigits(gps.date.day()) + "T" +
                         twoDigits(gps.time.hour()) + ":" +
                         twoDigits(gps.time.minute()) + ":" +
                         twoDigits(gps.time.second()) + "." +
                         String(gps.time.centisecond() * 10) + "Z";

      // Crea JSON
      snprintf(payload, sizeof(payload),
               "{\"Latitude\":%.6f,\"Longitude\":%.6f,\"CreatedAt\":\"%s,"vechicleId":1\"}",
               lat, lon, timestamp.c_str());

      // Pubblica su MQTT
      client.publish("gps/coordinate", payload);

      // Debug seriale
      Serial.println(payload);

    } else {
      Serial.println("GPS non ha ancora fix, attesa...");
    }
  }
}
