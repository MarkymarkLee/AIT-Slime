#include <WiFi.h>

const int micPin = A0;
const int relayPin = D5;  // ESP32使用GPIO5

// WiFi 設定 - 請修改為您的WiFi資訊
const char* ssid = "";          // 改成您的WiFi SSID
const char* password = "";      // 改成您的WiFi密碼

// TCP 伺服器設定
WiFiServer server(8080);
WiFiClient client;
bool clientConnected = false;

void setup() {
  pinMode(relayPin, OUTPUT);
  digitalWrite(relayPin, LOW);  // 一開始氣泵關閉
  Serial.begin(115200);
  
  // 連接WiFi
  Serial.println("🔧 ESP32 Station模式啟動");
  Serial.print("正在連接WiFi: ");
  Serial.println(ssid);
  
  WiFi.begin(ssid, password);
  
  // 等待連接
  int timeout = 0;
  while (WiFi.status() != WL_CONNECTED && timeout < 20) {
    delay(500);
    Serial.print(".");
    timeout++;
  }
  
  if (WiFi.status() == WL_CONNECTED) {
    Serial.println("\n✅ WiFi連接成功!");
    Serial.print("ESP32 IP地址: ");
    Serial.println(WiFi.localIP());
    Serial.print("子網路遮罩: ");
    Serial.println(WiFi.subnetMask());
    Serial.print("預設閘道: ");
    Serial.println(WiFi.gatewayIP());
  } else {
    Serial.println("\n❌ WiFi連接失敗!");
    Serial.println("請檢查WiFi名稱和密碼");
    return;
  }
  
  // 啟動TCP伺服器
  server.begin();
  Serial.println("🌐 TCP伺服器已啟動在端口 8080");
  Serial.println("等待Python客戶端連接...");
  Serial.println("=================================");
  
  // 顯示連接資訊
  Serial.println("📋 Python設定資訊:");
  Serial.print("TCP_HOST = '");
  Serial.print(WiFi.localIP());
  Serial.println("'");
  Serial.println("TCP_PORT = 8080");
  Serial.println("=================================");
}

void loop() {
  // 檢查WiFi連接狀態
  if (WiFi.status() != WL_CONNECTED) {
    Serial.println("⚠️ WiFi連接中斷！重新連接中...");
    WiFi.begin(ssid, password);
    delay(5000);
    return;
  }
  
  // 檢查是否有新的客戶端連接
  if (!clientConnected) {
    client = server.available();
    if (client) {
      Serial.println("✅ Python客戶端已連接");
      Serial.print("客戶端IP: ");
      Serial.println(client.remoteIP());
      clientConnected = true;
    }
  }
  
  // 如果有客戶端連接
  if (clientConnected && client.connected()) {
    // 1. 讀取麥克風數值並傳給 Python
    int micValue = analogRead(micPin);
    client.println(micValue);  // 透過TCP發送數據
    
    // 2. 檢查是否有從 Python 傳來的指令
    if (client.available() > 0) {
      char input = client.read(); 
      if (input == 's') {
        digitalWrite(relayPin, HIGH);  // 吸氣 -> 開啟氣泵
        Serial.println("🌪️ 氣泵開啟 (指令: s)");
      } else if (input == 'x') {
        digitalWrite(relayPin, LOW);   // 吐氣 -> 關閉氣泵
        Serial.println("⏹️ 氣泵關閉 (指令: x)");
      }
    }
  } else if (clientConnected) {
    // 客戶端斷線了
    Serial.println("❌ Python客戶端斷線");
    clientConnected = false;
    client.stop();
  }
  
  delay(2);  // 每秒約 500 筆數據
}

// 每30秒顯示一次狀態信息
void printStatus() {
  static unsigned long lastStatus = 0;
  if (millis() - lastStatus > 30000) {
    Serial.println("\n📊 系統狀態:");
    Serial.print("WiFi狀態: ");
    Serial.println(WiFi.status() == WL_CONNECTED ? "已連接" : "斷線");
    Serial.print("IP地址: ");
    Serial.println(WiFi.localIP());
    Serial.print("客戶端連接: ");
    Serial.println(clientConnected ? "是" : "否");
    Serial.print("訊號強度: ");
    Serial.print(WiFi.RSSI());
    Serial.println(" dBm");
    Serial.println("===================\n");
    lastStatus = millis();
  }
}