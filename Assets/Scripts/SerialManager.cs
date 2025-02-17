using UnityEngine;
using System;
using System.Collections;

public class SerialManager : MonoBehaviour, ISerialInputOutputManagerListener
{
    public event Action<string> OnDataReceived;
    public event Action<string> OnError;
    public event Action OnConnected;

    private const string k_usbPermission = "com.example.app.USB_PERMISSION";
    private const int k_baudRate = 9600;

    private AndroidJavaObject m_usbManager;
    private AndroidJavaObject m_usbDevice;
    private AndroidJavaObject m_usbSerialPort;
    private AndroidJavaObject m_serialIoManager;
    private AndroidJavaObject m_permissionReceiver;
    private AndroidJavaObject m_driver;
    private bool m_isConnected;

    private void Start()
    {
        if (Application.platform != RuntimePlatform.Android)
        {
            OnError?.Invoke("This feature is only available on Android");
            return;
        }

        StartCoroutine(InitializeSerial());
    }

    private void OnDestroy()
    {
        CleanupConnections();
    }

    public void OnNewData(byte[] data)
    {
        if (data != null && data.Length > 0)
        {
            string received = System.Text.Encoding.UTF8.GetString(data);
            OnDataReceived?.Invoke(received);
            Debug.Log($"Data received: {received}");
        }
    }

    public void OnRunError(Exception e)
    {
        OnError?.Invoke($"I/O Error: {e.Message}");
        Debug.LogError($"I/O Error: {e.Message}");
    }

    public void Write(string data)
    {
        if (!m_isConnected || m_usbSerialPort == null)
        {
            OnError?.Invoke("Device not connected");
            return;
        }

        try
        {
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(data);
            m_usbSerialPort.Call("write", bytes, 1000); // timeout of 1000ms
        }
        catch (Exception ex)
        {
            OnError?.Invoke($"Error during writing: {ex.Message}");
        }
    }

    private IEnumerator InitializeSerial()
    {
        try
        {
            using (AndroidJavaClass unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (AndroidJavaObject activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
            {
                m_usbManager = activity.Call<AndroidJavaObject>("getSystemService", "usb");

                AndroidJavaObject prober = new AndroidJavaClass("com.hoho.android.usbserial.driver.UsbSerialProber")
                    .CallStatic<AndroidJavaObject>("getDefaultProber");

                AndroidJavaObject availableDrivers = prober.Call<AndroidJavaObject>("findAllDrivers", m_usbManager);

                if (availableDrivers.Call<int>("size") == 0)
                {
                    OnError?.Invoke("No serial driver found");
                    yield break;
                }

                m_driver = availableDrivers.Call<AndroidJavaObject>("get", 0);
                m_usbDevice = m_driver.Call<AndroidJavaObject>("getDevice");
            }
        }
        catch (Exception ex)
        {
            OnError?.Invoke($"Error during initialization: {ex.Message}");
        }
        while (!m_usbManager.Call<bool>("hasPermission", m_usbDevice))
        {
            RequestUsbPermission();
            yield return new WaitForSeconds(1);
        }
        ConnectToDevice(m_driver);
    }

    private void RequestUsbPermission()
    {
        try
        {
            using (AndroidJavaClass unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (AndroidJavaObject activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
            {
                AndroidJavaObject intent = new AndroidJavaObject("android.content.Intent", k_usbPermission);

                int flagMutable = 0x02000000;
                AndroidJavaObject pendingIntent = new AndroidJavaClass("android.app.PendingIntent")
                    .CallStatic<AndroidJavaObject>("getBroadcast", activity, 0, intent, flagMutable);

                m_usbManager.Call("requestPermission", m_usbDevice, pendingIntent);
            }
        }
        catch (Exception ex)
        {
            OnError?.Invoke($"Error requesting permissions: {ex.Message}");
        }
    }

    private void ConnectToDevice(AndroidJavaObject driver)
    {
        try
        {
            AndroidJavaObject connection = m_usbManager.Call<AndroidJavaObject>("openDevice", m_usbDevice);
            if (connection == null)
            {
                OnError?.Invoke("Unable to open USB connection");
                return;
            }

            AndroidJavaObject ports = driver.Call<AndroidJavaObject>("getPorts");
            m_usbSerialPort = ports.Call<AndroidJavaObject>("get", 0);

            m_usbSerialPort.Call("open", connection);
            m_usbSerialPort.Call("setParameters", k_baudRate, 8, 1, 0);

            InitializeIoManager();

            m_isConnected = true;
            Debug.Log("USB connection established");
            OnConnected?.Invoke();
        }
        catch (Exception ex)
        {
            OnError?.Invoke($"Error during connection: {ex.Message}");
            Debug.LogError($"Error during connection: {ex.Message}");
        }
    }

    private void InitializeIoManager()
    {
        m_serialIoManager = new AndroidJavaObject(
            "com.hoho.android.usbserial.util.SerialInputOutputManager",
            m_usbSerialPort,
            new SerialInputOutputManagerListenerProxy(this)
        );

        AndroidJavaObject executorService = new AndroidJavaClass("java.util.concurrent.Executors")
            .CallStatic<AndroidJavaObject>("newSingleThreadExecutor");
        executorService.Call("execute", m_serialIoManager);
    }

    private void CleanupConnections()
    {
        if (m_isConnected)
        {
            try
            {
                if (m_serialIoManager != null)
                {
                    m_serialIoManager.Call("stop");
                }
                if (m_usbSerialPort != null)
                {
                    m_usbSerialPort.Call("close");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error during closing the connection: {ex.Message}");
            }
            m_isConnected = false;
        }

        if (m_permissionReceiver != null)
        {
            try
            {
                using (AndroidJavaClass unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (AndroidJavaObject activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
                {
                    activity.Call("unregisterReceiver", m_permissionReceiver);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error during unregistering the receiver: {ex.Message}");
            }
        }
    }
}

public class SerialInputOutputManagerListenerProxy : AndroidJavaProxy
{
    private readonly ISerialInputOutputManagerListener m_Listener;

    public SerialInputOutputManagerListenerProxy(ISerialInputOutputManagerListener listener)
        : base("com.hoho.android.usbserial.util.SerialInputOutputManager$Listener")
    {
        m_Listener = listener;
    }

    public void onNewData(byte[] data)
    {
        m_Listener.OnNewData(data);
    }

    public void onRunError(AndroidJavaObject exception)
    {
        string message = exception.Call<string>("getMessage");
        m_Listener.OnRunError(new Exception(message));
    }
}

public interface ISerialInputOutputManagerListener
{
    void OnNewData(byte[] data);
    void OnRunError(Exception e);
}
