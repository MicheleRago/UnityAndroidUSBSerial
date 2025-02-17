using System;
using UnityEngine;
using UnityEngine.UI;

public class SerialExample : MonoBehaviour
{
    [SerializeField]
    private Text m_text;
    [SerializeField]
    private Text m_inputText;

    private SerialManager serialManager;

    void Start()
    {
        serialManager = GetComponent<SerialManager>();
        serialManager.OnDataReceived += (data) => {
            m_text.text = $"[{DateTime.Now:HH:mm:ss}] Data received: {data}\n" + m_text.text;
        };

        serialManager.OnError += (error) => {
            m_text.text = $"[{DateTime.Now:HH:mm:ss}] Error: {error}\n" + m_text.text;
        };

        serialManager.OnConnected += () => {
            m_text.text = $"[{DateTime.Now:HH:mm:ss}] Device connected!\n" + m_text.text;
        };
    }

    public void SendText()
    {
        serialManager.Write(m_inputText.text + "\n");
    }
}
