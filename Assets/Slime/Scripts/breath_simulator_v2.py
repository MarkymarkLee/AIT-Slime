import socket
import threading
import time
import json
import argparse
from pynput import keyboard
import queue

class BreathSimulatorV2:
    def __init__(self, mode='breath_control'):
        """
        初始化呼吸模擬器
        mode: 'breath_control' (Python主導) 或 'unity_control' (Unity主導)
        """
        self.mode = mode  # 'breath_control' 或 'unity_control'
        
        # 呼吸狀態
        self.current_breath_state = 'undecided'
        self.unity_character_state = 'normal'  # Unity角色狀態：'normal', 'enlarged', 'shrunken'
        
        # TCP設定 - 與Unity通訊
        self.unity_host = 'localhost'
        self.unity_port = 7777
        self.unity_socket = None
        self.unity_client = None
        
        # ESP32模擬設定
        self.esp32_host = 'localhost'
        self.esp32_port = 8080
        self.esp32_socket = None
        
        # 消息隊列
        self.message_queue = queue.Queue()
        
        # 狀態標記
        self.running = True
        
        # 氣泵狀態
        self.pump_is_on = False
        
        print(f"🎮 啟動模式: {'Python呼吸控制' if mode == 'breath_control' else 'Unity角色控制'}")
        
    def start_unity_server(self):
        """啟動TCP伺服器等待Unity連接"""
        try:
            self.unity_socket = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
            self.unity_socket.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
            self.unity_socket.bind((self.unity_host, self.unity_port))
            self.unity_socket.listen(1)
            print(f"🌐 等待Unity連接於 {self.unity_host}:{self.unity_port}")
            
            self.unity_client, addr = self.unity_socket.accept()
            print(f"✅ Unity已連接: {addr}")
            
            # 發送模式資訊給Unity
            self.send_mode_info()
            
            # 啟動Unity通訊線程
            unity_thread = threading.Thread(target=self.handle_unity_communication, daemon=True)
            unity_thread.start()
            
        except Exception as e:
            print(f"❌ Unity伺服器啟動失敗: {e}")
    
    def send_mode_info(self):
        """發送模式資訊給Unity"""
        message = {
            'type': 'mode_setup',
            'mode': self.mode,
            'description': 'Python呼吸控制模式' if self.mode == 'breath_control' else 'Unity角色控制模式'
        }
        self.message_queue.put(message)
    
    def handle_unity_communication(self):
        """處理與Unity的雙向通訊"""
        while self.running:
            try:
                # 接收Unity資料
                if self.unity_client:
                    self.unity_client.settimeout(0.1)
                    try:
                        data = self.unity_client.recv(1024)
                        if data:
                            message = json.loads(data.decode('utf-8'))
                            self.handle_unity_message(message)
                    except socket.timeout:
                        pass
                    except Exception as e:
                        print(f"⚠️ 接收Unity資料錯誤: {e}")
                
                # 發送資料給Unity
                if not self.message_queue.empty():
                    message = self.message_queue.get()
                    if self.unity_client:
                        try:
                            self.unity_client.send(json.dumps(message).encode('utf-8'))
                        except Exception as e:
                            print(f"⚠️ 發送資料給Unity失敗: {e}")
                
                time.sleep(0.01)  # 避免CPU過載
                
            except Exception as e:
                print(f"❌ Unity通訊錯誤: {e}")
                break
    
    def handle_unity_message(self, message):
        """處理Unity傳來的消息"""
        if self.mode == 'unity_control':
            # Unity控制模式：接收Unity的角色狀態
            if message.get('type') == 'character_state':
                old_state = self.unity_character_state
                self.unity_character_state = message.get('state', 'normal')
                
                # 根據狀態變化控制氣泵
                if old_state != self.unity_character_state:
                    if self.unity_character_state == 'enlarged':
                        self.control_pump(True)  # 角色變大時開氣泵
                        print(f"🎮 Unity狀態變化: {old_state} → {self.unity_character_state} [開啟氣泵]")
                    else:
                        self.control_pump(False)  # 其他狀態關氣泵
                        print(f"🎮 Unity狀態變化: {old_state} → {self.unity_character_state} [關閉氣泵]")
                
        # 兩種模式都可以接收的通用消息
        elif message.get('type') == 'ping':
            # 回應ping
            response = {'type': 'pong', 'timestamp': time.time()}
            self.message_queue.put(response)
    
    def send_to_unity(self, breath_state, source='keyboard'):
        """發送呼吸狀態給Unity（僅在breath_control模式）"""
        if self.mode != 'breath_control':
            return
            
        message = {
            'type': 'breath_update',
            'state': breath_state,
            'source': source,
            'timestamp': time.time()
        }
        self.message_queue.put(message)
    
    def control_pump(self, turn_on):
        """控制氣泵開關"""
        if turn_on and not self.pump_is_on:
            self.send_to_esp32('s')
            self.pump_is_on = True
        elif not turn_on and self.pump_is_on:
            self.send_to_esp32('x')
            self.pump_is_on = False
    
    def send_to_esp32(self, command):
        """發送控制命令到ESP32（模擬）"""
        action = "開啟氣泵" if command == 's' else "關閉氣泵"
        print(f"📤 [ESP32模擬] 發送命令: {command} ({action})")
        # 這裡可以加入實際的ESP32通訊邏輯
    
    def on_key_press(self, key):
        """鍵盤按鍵處理（僅在breath_control模式有效）"""
        if self.mode != 'breath_control':
            return
            
        try:
            if key.char == 'i' or key.char == 'I':
                self.current_breath_state = 'likely_INHALE'
                print("🫁 鍵盤輸入: INHALE (吸氣)")
                self.send_to_unity(self.current_breath_state, 'keyboard')
                self.control_pump(False)  # 吸氣時關閉氣泵
                
            elif key.char == 'e' or key.char == 'E':
                self.current_breath_state = 'likely_EXHALE'
                print("💨 鍵盤輸入: EXHALE (吐氣)")
                self.send_to_unity(self.current_breath_state, 'keyboard')
                self.control_pump(True)   # 吐氣時開啟氣泵
                
            elif key.char == 'u' or key.char == 'U':
                self.current_breath_state = 'undecided'
                print("❓ 鍵盤輸入: UNDECIDED (未決定)")
                self.send_to_unity(self.current_breath_state, 'keyboard')
                self.control_pump(False)  # 未決定時關閉氣泵
                
        except AttributeError:
            # 特殊按鍵（如Ctrl, Alt等）
            if key == keyboard.Key.esc:
                print("🛑 程式結束")
                self.running = False
                return False
    
    def display_status(self):
        """顯示當前狀態"""
        if self.mode == 'breath_control':
            pump_status = "🌪️ 開啟" if self.pump_is_on else "⏹️ 關閉"
            print(f"\r🎯 呼吸狀態: {self.current_breath_state:<16} | 氣泵: {pump_status}", end='', flush=True)
        else:
            pump_status = "🌪️ 開啟" if self.pump_is_on else "⏹️ 關閉"
            print(f"\r🎮 Unity角色: {self.unity_character_state:<12} | 氣泵: {pump_status}", end='', flush=True)
    
    def control_loop(self):
        """主控制迴圈"""
        if self.mode == 'breath_control':
            print("🎮 呼吸控制模式:")
            print("   I 鍵 = INHALE (吸氣) - 關閉氣泵")
            print("   E 鍵 = EXHALE (吐氣) - 開啟氣泵") 
            print("   U 鍵 = UNDECIDED (未決定) - 關閉氣泵")
            print("   ESC = 結束程式")
            print("   Python控制呼吸，Unity顯示狀態")
        else:
            print("🎮 Unity控制模式:")
            print("   Unity控制角色縮放")
            print("   角色變大時自動開啟氣泵")
            print("   角色變小或正常時關閉氣泵")
            print("   ESC = 結束程式")
            
        print("-" * 50)
        
        while self.running:
            time.sleep(0.1)
            self.display_status()
    
    def run(self):
        """啟動模擬器"""
        print("🚀 呼吸模擬器 V2 啟動中...")
        
        # 啟動Unity伺服器
        unity_server_thread = threading.Thread(target=self.start_unity_server, daemon=True)
        unity_server_thread.start()
        
        # 等待Unity連接
        time.sleep(2)
        
        if self.mode == 'breath_control':
            # 呼吸控制模式：啟動鍵盤監聽
            print("⌨️ 啟動鍵盤監聽...")
            with keyboard.Listener(on_press=self.on_key_press) as listener:
                # 啟動控制迴圈
                control_thread = threading.Thread(target=self.control_loop, daemon=True)
                control_thread.start()
                
                listener.join()
        else:
            # Unity控制模式：只運行控制迴圈
            try:
                self.control_loop()
            except KeyboardInterrupt:
                print("\n🛑 程式結束")
                self.running = False
        
        # 清理資源
        self.cleanup()
    
    def cleanup(self):
        """清理資源"""
        self.running = False
        self.control_pump(False)  # 確保氣泵關閉
        
        if self.unity_client:
            self.unity_client.close()
        if self.unity_socket:
            self.unity_socket.close()
        print("\n🧹 資源清理完成")

def main():
    parser = argparse.ArgumentParser(description='呼吸檢測模擬器 V2')
    parser.add_argument('--mode', choices=['breath_control', 'unity_control'], 
                        default='breath_control',
                        help='選擇運行模式: breath_control (Python主導) 或 unity_control (Unity主導)')
    
    args = parser.parse_args()
    
    print("🎯 呼吸檢測模擬器 V2")
    print(f"🎮 模式: {args.mode}")
    
    simulator = BreathSimulatorV2(mode=args.mode)
    simulator.run()

if __name__ == "__main__":
    main() 