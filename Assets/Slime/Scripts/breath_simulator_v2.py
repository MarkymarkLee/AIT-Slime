import socket
import threading
import time
import json
import argparse
from pynput import keyboard
import queue
import numpy as np
import ipaddress

class BreathSimulatorV2:
    def __init__(self, mode='breath_control', esp32_host=None, esp32_port=8080, unity_port=7777):
        """
        初始化呼吸模擬器
        mode: 'breath_control' (Python主導) 或 'unity_control' (Unity主導) 或 'breath_detection' (真實呼吸檢測)
        esp32_host: ESP32 的 IP 位址 (可選)
        esp32_port: ESP32 的埠號 (可選，預設 8080)
        unity_port: Unity 的埠號 (可選，預設 7777)
        """
        self.mode = mode  # 'breath_control', 'unity_control', 或 'breath_detection'
        
        # 呼吸狀態
        self.current_breath_state = 'undecided'
        self.unity_character_state = 'normal'  # Unity角色狀態：'normal', 'enlarged', 'shrunken'
        
        # TCP設定 - 與Unity通訊
        self.unity_host = 'localhost'
        self.unity_port = unity_port
        self.unity_socket = None
        self.unity_client = None
        
        # ESP32真實設定
        self.esp32_host = esp32_host
        self.esp32_port = esp32_port
        self.esp32_socket = None
        
        # 消息隊列
        self.message_queue = queue.Queue()
        
        # 狀態標記
        self.running = True
        
        # 氣泵狀態
        self.pump_is_on = False
        
        # 呼吸檢測相關參數
        self.samplerate = 500
        self.block_duration = 0.25
        self.block_size = int(self.samplerate * self.block_duration)
        self.baseline = 0
        self.adc_range = 4095
        self.buffer = []
        self.data_buffer = ""
        self.rms_history = []
        self.inhale_history = []
        
        print(f"🎮 啟動模式: {self._get_mode_description()}")
        
    def _get_mode_description(self):
        """取得模式描述"""
        descriptions = {
            'breath_control': 'Python呼吸控制',
            'unity_control': 'Unity角色控制(真實ESP32)',
            'breath_detection': '真實呼吸檢測'
        }
        return descriptions.get(self.mode, '未知模式')

    def setup_esp32_connection(self):
        """設置ESP32連接"""
        if self.mode not in ['breath_detection', 'unity_control']:
            return True
            
        print(f"🔧 設置ESP32連接 ({self.mode}模式)")
        
        if not self.esp32_host:
            self.esp32_host = "192.168.1.129"
        
        return self.create_esp32_connection()

    def create_esp32_connection(self):
        """建立ESP32 TCP連接"""
        try:
            self.esp32_socket = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
            self.esp32_socket.connect((self.esp32_host, self.esp32_port))
            self.esp32_socket.settimeout(1.0)
            print(f"✅ ESP32連接成功: {self.esp32_host}:{self.esp32_port}")
            return True
        except Exception as e:
            print(f"❌ ESP32連接失敗: {e}")
            return False

    def calibrate_baseline(self):
        """校正基準值"""
        if self.mode != 'breath_detection' or not self.esp32_socket:
            return
            
        print("⏳ 校正中，請保持安靜...")
        baseline_samples = []
        
        while len(baseline_samples) < 200:
            try:
                data = self.esp32_socket.recv(1024).decode('utf-8')
                if data:
                    self.data_buffer += data
                    lines = self.data_buffer.split('\n')
                    self.data_buffer = lines[-1]
                    
                    for line in lines[:-1]:
                        try:
                            val = int(line.strip())
                            baseline_samples.append(val)
                            if len(baseline_samples) % 50 == 0:
                                print(f"📊 校正進度: {len(baseline_samples)}/200")
                        except:
                            continue
            except socket.timeout:
                continue
            except Exception as e:
                print(f"❌ 校正錯誤: {e}")
                break
        
        if baseline_samples:
            self.baseline = np.mean(baseline_samples)
            adc_min = min(baseline_samples)
            adc_max = max(baseline_samples)
            
            # 判斷是否為 10-bit 或 12-bit
            if adc_max > 2048:
                self.adc_range = 4095
            else:
                self.adc_range = 1023
            
            print(f"✅ 基準值：{self.baseline:.2f}，ADC 範圍：0–{self.adc_range}")

    # === 呼吸檢測算法 ===
    def breath_strength(self, amp):
        """呼吸強度分級"""
        if amp < 0.3:
            return "💧 微弱"
        elif amp < 0.6:
            return "💨 中等"
        elif amp < 0.9:
            return "🌪️ 強力"
        else:
            return "🚨 超強"

    def classify_nose_breath(self, signal):
        """分類鼻腔呼吸"""
        # === 特徵計算 ===
        zcr = np.mean(np.diff(np.sign(signal)) != 0)
        amp = np.max(np.abs(signal))
        rms = np.sqrt(np.mean(signal ** 2))

        # === 雜訊過濾條件（AMP 太低就略過）===
        if amp < 0.1:
            self.inhale_history = []
            return 'undecided', rms, amp, zcr, 0, 0, 0

        # === 吸氣條件：ZCR 在 0.4~0.55 且 RMS < 0.15 ===
        is_inhale = (0. <= zcr) and (rms < 0.15)

        # 吸氣狀態追蹤（持續記錄近 3 次）
        self.inhale_history.append(is_inhale)
        if len(self.inhale_history) > 3:
            self.inhale_history.pop(0)

        # === 判斷邏輯 ===
        if all(self.inhale_history):  # 連續吸氣特徵成立
            decision = 'likely_INHALE'
        elif rms > 0.3:          # 吹氣只看 RMS > 0.3
            decision = 'likely_EXHALE'
        else:
            decision = 'undecided'

        return decision, rms, amp, zcr, 0, 0, 0

    def process_breath_detection(self):
        """處理呼吸檢測"""
        if self.mode != 'breath_detection' or not self.esp32_socket:
            return
            
        try:
            data = self.esp32_socket.recv(1024).decode('utf-8')
            if data:
                self.data_buffer += data
                lines = self.data_buffer.split('\n')
                self.data_buffer = lines[-1]
                
                for line in lines[:-1]:
                    try:
                        val = int(line.strip())
                        # 正規化
                        norm_val = (val - self.baseline) / self.adc_range
                        norm_val = max(min(norm_val, 1.0), -1.0)
                        self.buffer.append(norm_val)
                    except:
                        continue
        except socket.timeout:
            pass
        except Exception as e:
            print(f"❌ 接收數據錯誤: {e}")

        # 如果緩衝區有足夠的數据，進行分析
        if len(self.buffer) >= self.block_size:
            signal = np.array(self.buffer[:self.block_size])
            self.buffer = self.buffer[self.block_size:]

            old_state = self.current_breath_state
            self.current_breath_state, rms, max_amp, zcr, low_energy, high_energy, total_energy = self.classify_nose_breath(signal)
            strength = self.breath_strength(max_amp)

            # 如果狀態改變，發送給Unity
            if old_state != self.current_breath_state:
                self.send_to_unity(self.current_breath_state, 'breath_detection')

            # 控制氣泵
            command_sent = ""
            if self.current_breath_state == 'likely_INHALE':
                if not self.pump_is_on:
                    self.control_pump(True)
                    command_sent = " [🌪️ 開啟氣泵]"
                else:
                    command_sent = " [🌪️ 氣泵保持開啟]"
            elif self.current_breath_state == 'likely_EXHALE':
                if self.pump_is_on:
                    self.control_pump(False)
                    command_sent = " [⏹️ 關閉氣泵]"
                else:
                    command_sent = " [⏹️ 氣泵保持關閉]"
            elif self.current_breath_state == 'undecided':
                if self.pump_is_on:
                    command_sent = " [🌪️ 氣泵保持開啟]"
                else:
                    command_sent = " [⏹️ 氣泵保持關閉]"

            print(f"🎯 {self.current_breath_state:<16} | RMS={rms:.4f} AMP={max_amp:.4f} ZCR={zcr:.4f} | "
                  f"Low={low_energy:.2f} High={high_energy:.2f} Total={total_energy:.2f} | {strength}{command_sent}")

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
            'description': self._get_mode_description()
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
        """發送呼吸狀態給Unity"""
        if self.mode not in ['breath_control', 'breath_detection']:
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
        """發送控制命令到ESP32"""
        action = "開啟氣泵" if command == 's' else "關閉氣泵"
        
        if self.mode in ['breath_detection', 'unity_control'] and self.esp32_socket:
            # 真實模式：發送到真實ESP32
            try:
                self.esp32_socket.send(command.encode())
                print(f"📤 [ESP32真實] 發送命令: {command} ({action})")
            except Exception as e:
                print(f"❌ 發送命令失敗: {e}")
        else:
            # 模擬模式
            print(f"📤 [ESP32模擬] 發送命令: {command} ({action})")
    
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
        pump_status = "🌪️ 開啟" if self.pump_is_on else "⏹️ 關閉"
        
        if self.mode == 'breath_control':
            print(f"\r🎯 呼吸狀態: {self.current_breath_state:<16} | 氣泵: {pump_status}", end='', flush=True)
        elif self.mode == 'unity_control':
            print(f"\r🎮 Unity角色: {self.unity_character_state:<12} | 氣泵: {pump_status}", end='', flush=True)
        elif self.mode == 'breath_detection':
            print(f"\r🔍 呼吸檢測: {self.current_breath_state:<16} | 氣泵: {pump_status}", end='', flush=True)
    
    def control_loop(self):
        """主控制迴圈"""
        if self.mode == 'breath_control':
            print("🎮 呼吸控制模式:")
            print("   I 鍵 = INHALE (吸氣) - 關閉氣泵")
            print("   E 鍵 = EXHALE (吐氣) - 開啟氣泵") 
            print("   U 鍵 = UNDECIDED (未決定) - 關閉氣泵")
            print("   ESC = 結束程式")
            print("   Python控制呼吸，Unity顯示狀態")
        elif self.mode == 'unity_control':
            print("🎮 Unity控制模式:")
            print("   Unity控制角色縮放")
            print("   角色變大時自動開啟氣泵")
            print("   角色變小或正常時關閉氣泵")
            print("   實際發送指令到ESP32設備")
            print("   ESC = 結束程式")
        elif self.mode == 'breath_detection':
            print("🔍 真實呼吸檢測模式:")
            print("   自動檢測呼吸狀態")
            print("   吸氣時關閉氣泵，吐氣時開啟氣泵")
            print("   Ctrl+C = 結束程式")
            
        print("-" * 50)
        
        while self.running:
            if self.mode == 'breath_detection':
                self.process_breath_detection()
            
            elif self.mode == 'breath_control':
                pass

            elif self.mode == 'unity_control':
                # Unity控制模式：等待Unity發送狀態
                if self.unity_client:
                    try:
                        data = self.unity_client.recv(1024)
                        if data:
                            message = json.loads(data.decode('utf-8'))
                            self.handle_unity_message(message)
                    except socket.timeout:
                        pass
                    except Exception as e:
                        print(f"⚠️ Unity通訊錯誤: {e}")
            time.sleep(0.1)
            self.display_status()
    
    def run(self):
        """啟動模擬器"""
        print("🚀 呼吸模擬器 V2 啟動中...")
        
        # 啟動Unity伺服器
        unity_server_thread = threading.Thread(target=self.start_unity_server, daemon=True)
        unity_server_thread.start()
        
        # 如果是呼吸檢測模式或Unity控制模式，設置ESP32連接
        if self.mode in ['breath_detection', 'unity_control']:
            if not self.setup_esp32_connection():
                print("❌ ESP32連接失敗，程式結束")
                return
            
            # 只有呼吸檢測模式需要校正
            if self.mode == 'breath_detection':
                self.calibrate_baseline()
        
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
            # Unity控制模式或呼吸檢測模式：只運行控制迴圈
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
        if self.esp32_socket:
            self.esp32_socket.close()
        print("\n🧹 資源清理完成")

def main():
    parser = argparse.ArgumentParser(description='呼吸檢測模擬器 V2')
    parser.add_argument('--mode', choices=['breath_control', 'unity_control', 'breath_detection'], 
                        default='breath_control',
                        help='選擇運行模式: breath_control (Python主導), unity_control (Unity主導+真實ESP32), 或 breath_detection (真實呼吸檢測)')
    parser.add_argument('--esp32_host', type=str, default="192.168.1.129", help='ESP32 的 IP 位址 (可選)')
    parser.add_argument('--unity_port', type=int, default=7777, help='Unity 的埠號 (可選，預設 7777)')
    args = parser.parse_args()
    print("🎯 呼吸檢測模擬器 V2")
    print(f"🎮 模式: {args.mode}")
    simulator = BreathSimulatorV2(mode=args.mode, esp32_host=args.esp32_host, unity_port=args.unity_port)
    simulator.run()

if __name__ == "__main__":
    main()
