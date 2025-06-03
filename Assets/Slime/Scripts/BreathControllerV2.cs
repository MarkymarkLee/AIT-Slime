using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;
using Newtonsoft.Json;
using TMPro;

public class BreathControllerV2 : MonoBehaviour
{
    [Header("連接設定")]
    public string pythonHost = "localhost";
    public int pythonPort = 7777;
    public string ESP32_Host = "192.168.1.129";

    // 模式設定
    public enum Mode { breath_control, unity_control, breath_detection }
    public Mode currentMode = Mode.unity_control;


    // 網路相關
    private TcpClient tcpClient;
    private NetworkStream stream;
    private Thread tcpListenerThread;
    private bool isConnected = false;
    private bool shouldStop = false;
    
    // 狀態管理
    public enum CharacterState { normal, enlarged, shrunken }
    public CharacterState currentCharacterState = CharacterState.normal;
    private string currentBreathState = "undecided";
    
    // 消息隊列（線程安全）
    private Queue<string> messageQueue = new Queue<string>();
    private readonly object queueLock = new object();
    
    
    void Start()
    {
        // 連接到Python
        ConnectToPython();
    }
    
    void Update()
    {
        // 處理網路消息
        ProcessMessages();
        
        // 根據模式處理輸入
        if (currentMode == Mode.unity_control)
        {
            
        }
    }
    
    public void UpdateCharacterState(CharacterState newState)
    {   
        if (currentMode != Mode.unity_control)
        {
            Debug.LogWarning("當前模式不是unity_control，無法更新角色狀態");
            return;
        }

        if (newState == CharacterState.enlarged)
        {
            Debug.Log("Unity控制: 角色放大");
        }
        else if (newState == CharacterState.shrunken)
        {
            Debug.Log("Unity控制: 角色縮小");
        }
        else
        {
            Debug.Log("Unity控制: 角色恢復正常");
        }
        
        // 如果狀態改變，發送給Python
        if (newState != currentCharacterState)
        {
            currentCharacterState = newState;
            SendCharacterState(currentCharacterState.ToString());
        }
    }
    
    void ProcessMessages()
    {
        lock (queueLock)
        {
            while (messageQueue.Count > 0)
            {
                string message = messageQueue.Dequeue();
                ProcessPythonMessage(message);
            }
        }
    }
    
    void ProcessPythonMessage(string jsonMessage)
    {
        try
        {
            var messageData = JsonConvert.DeserializeObject<Dictionary<string, object>>(jsonMessage);
            string messageType = messageData["type"].ToString();
            
            switch (messageType)
            {
                case "mode_setup":
                    // 接收模式設定
                    string pythonMode = messageData["mode"].ToString();
                    string description = messageData["description"].ToString();
                    Debug.Log($"收到模式設定: {pythonMode} - {description}");

                    // 將Python傳來的mode與Unity的Mode列舉同步
                    if (Enum.TryParse(pythonMode, out Mode parsedMode))
                    {
                        currentMode = parsedMode;
                    }
                    else
                    {
                        Debug.LogWarning($"未知的模式: {pythonMode}");
                    }
                    break;
                    
                case "breath_update":
                    // 接收呼吸狀態更新（僅在breath_control模式）
                    if (currentMode == Mode.breath_control || currentMode == Mode.breath_detection)
                    {
                        currentBreathState = messageData["state"].ToString();
                        string source = messageData["source"].ToString();
                        Debug.Log($"收到呼吸資料: {currentBreathState} (來源: {source})");
                        UpdateBreathState(currentBreathState);
                    }
                    break;
                    
                case "pong":
                    // 收到ping回應
                    Debug.Log("收到Python pong回應");
                    break;
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"處理Python消息錯誤: {e.Message}");
        }
    }
    
    void UpdateBreathState(string breathState)
    {
        switch (breathState)
        {
            case "likely_INHALE":
                // 吸氣時角色收縮
                currentCharacterState = CharacterState.shrunken;
                break;
            case "likely_EXHALE":
                // 吐氣時角色膨脹
                currentCharacterState = CharacterState.enlarged;
                break;
            case "undecided":
                // 未決定時回到原始大小
                currentCharacterState = CharacterState.normal;
                break;
        }
    }

    void ConnectToPython()
    {
        try
        {
            tcpListenerThread = new Thread(new ThreadStart(TCPConnect));
            tcpListenerThread.IsBackground = true;
            tcpListenerThread.Start();
        }
        catch (Exception e)
        {
            Debug.LogError($"連接Python失敗: {e.Message}");
        }
    }
    
    void TCPConnect()
    {
        try
        {
            // 啟動Python腳本
            string pythonExe = "python"; // 或 "python3"，視你的環境而定
            string scriptPath = Path.Combine(Application.dataPath, "Slime/Scripts/breath_simulator_v2.py"); // 根據實際路徑調整
            // 根據 currentMode 設定 Python 啟動參數
            string modeArg = currentMode.ToString().ToLower();
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = pythonExe,
                Arguments = $"\"{scriptPath}\" --mode {modeArg} --unity_port {pythonPort} --esp32_host {ESP32_Host}",
                UseShellExecute = true,
                CreateNoWindow = false,
            };
            try
            {
                System.Diagnostics.Process.Start(psi);
                Debug.Log($"已啟動Python腳本: {scriptPath}");
                Thread.Sleep(2000); // 等待1秒讓Python伺服器啟動
            }
            catch (Exception e)
            {
                Debug.LogError($"啟動Python腳本失敗: {e.Message}");
            }
            
            tcpClient = new TcpClient(pythonHost, pythonPort);
            stream = tcpClient.GetStream();
            isConnected = true;
            
            Debug.Log($"已連接到Python: {pythonHost}:{pythonPort}");
            
            // 持續監聽消息
            byte[] buffer = new byte[1024];
            StringBuilder messageBuilder = new StringBuilder();
            
            while (!shouldStop && tcpClient.Connected)
            {
                try
                {
                    int bytesRead = stream.Read(buffer, 0, buffer.Length);
                    if (bytesRead > 0)
                    {
                        string data = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                        messageBuilder.Append(data);
                        
                        // 處理完整的JSON消息
                        string messages = messageBuilder.ToString();
                        int braceCount = 0;
                        int startIndex = 0;
                        
                        for (int i = 0; i < messages.Length; i++)
                        {
                            if (messages[i] == '{') braceCount++;
                            else if (messages[i] == '}') braceCount--;
                            
                            if (braceCount == 0 && messages[i] == '}')
                            {
                                string completeMessage = messages.Substring(startIndex, i - startIndex + 1);
                                
                                lock (queueLock)
                                {
                                    messageQueue.Enqueue(completeMessage);
                                }
                                
                                startIndex = i + 1;
                            }
                        }
                        
                        // 保留未完整的消息
                        if (startIndex < messages.Length)
                        {
                            messageBuilder.Clear();
                            messageBuilder.Append(messages.Substring(startIndex));
                        }
                        else
                        {
                            messageBuilder.Clear();
                        }
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError($"接收資料錯誤: {e.Message}");
                    break;
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"TCP連接錯誤: {e.Message}");
        }
        finally
        {
            isConnected = false;
        }
    }
    
    void SendCharacterState(string state)
    {
        var message = new Dictionary<string, object>
        {
            ["type"] = "character_state",
            ["state"] = state,
            ["timestamp"] = Time.time
        };
        
        SendMessage(message);
    }
    
    void SendPing()
    {
        var message = new Dictionary<string, object>
        {
            ["type"] = "ping",
            ["timestamp"] = Time.time
        };
        
        SendMessage(message);
    }
    
    void SendMessage(Dictionary<string, object> message)
    {
        if (!isConnected || stream == null) return;
        
        try
        {
            string json = JsonConvert.SerializeObject(message);
            byte[] data = Encoding.UTF8.GetBytes(json);
            stream.Write(data, 0, data.Length);
            stream.Flush();
        }
        catch (Exception e)
        {
            Debug.LogError($"發送消息失敗: {e.Message}");
        }
    }
    
    void OnDestroy()
    {
        shouldStop = true;
        
        if (stream != null)
        {
            stream.Close();
        }
        
        if (tcpClient != null)
        {
            tcpClient.Close();
        }
        
        if (tcpListenerThread != null)
        {
            tcpListenerThread.Abort();
        }
    }
    
    void OnApplicationQuit()
    {
        OnDestroy();
    }
}