using System;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;


public class LivedataClient
{
    /// <summary>
    /// The point on wich to connect to
    /// </summary>
    private readonly int port;

    /// <summary>
    /// The address to connect to
    /// </summary>
    private readonly string ipAddress;

    /// <summary>
    /// Timeout before trying to check status and reconnect to server
    /// </summary>
    private readonly int reconnectTimeout;

    /// <summary>
    /// The characterstring to concider the end of the data transmission
    /// </summary>
    private readonly string _recievedataEndCharacters;

    /// <summary>
    /// Internal TcpClient to handle the coumication
    /// </summary>
    private TcpClient client;

    /// <summary>
    /// The stream we open
    /// </summary>
    private NetworkStream stream;

    /// <summary>
    /// Timer object to trigger a connection test
    /// </summary>
    private Timer _stateTimer;

    /// <summary>
    /// Internal flag in order to kill the workthread
    /// </summary>
    private bool _stopTask { get; set; }

    /// <summary>
    /// Internal reference to the worktrhead, not really neccessary.
    /// TODO: Remove this and remove the cancelation token.. The gracious dead of the task while loop should be enough
    /// </summary>
    private Task workTask { get; set; }


    /// <summary>
    /// Set up an event 
    /// </summary>
    public event EventHandler<EventArgs> ConnectionState;


    /// <summary>
    /// Set up an event for data recieved
    /// </summary>
    public event EventHandler<EventArgsT<string>> DataRecieved;

    /// <summary>
    /// Token to cancel the dowork thread if neccesary
    /// TODO: Remove this one, doesnt need to cancel, can just kill the while loops in the thread.
    /// </summary>
    private CancellationTokenSource TaskCancel;


    /// <summary>
    /// public property to read the current status of the connection
    /// TODO: Change this to a GET only
    /// </summary>
    public bool Connected { get; set; }



    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="port"></param>
    /// <param name="ipAddress"></param>
    /// <param name="reconnectTimeout">Time in milliseconds for trying to reconnect to server</param>
    public LivedataClient(int port, string ipAddress, int reconnectTimeout = 10000, string endCharacter = null)
    {
        this.port = port;
        this.ipAddress = ipAddress;
        this.reconnectTimeout = reconnectTimeout;
        this._recievedataEndCharacters = endCharacter;


        /// Set up timer to keep checking the connection state
        TimerCallback TimerDelegate = new TimerCallback(_checkState);
        _stateTimer = new Timer(TimerDelegate, null, 4000, this.reconnectTimeout);

        Start();
    }


    /// <summary>
    /// Internal method to check properties to see if the client is still connected and the tunel open.
    /// </summary>
    /// <param name="StateObj"></param>
    internal void _checkState(Object StateObj)
    {
        bool _connected = _isConnected();

        if (_connected != Connected)
        {
            Connected = _connected;
            RaiseConnectionState(Connected);
        }
        else
        {
            Connected = _connected;
        }


        if (!Connected)
            Start();
    }


    /// <summary>
    /// Disposes the stream and free the resources.
    /// </summary>
    private void _disposeStream()
    {
        if (stream != null)
        {
            stream.Close();
            stream.Dispose();
            stream = null;
        }
    }

    /// <summary>
    /// Closes all underlying TCP connections and free the resources of the Client
    /// </summary>
    private void _disposeClient()
    {
        if (client != null)
        {
            client.Close();
            client = null;
        }
    }


    /// <summary>
    /// Internal method to check the connection state.
    /// </summary>
    /// <returns></returns>
    internal bool _isConnected()
    {
        bool _connected = false;

        if (stream != null && client == null)
            _disposeStream();

        if (client != null && stream == null)
            stream = client.GetStream();

        if (client != null && stream != null)
        {

            if (stream.CanRead)
            {
                if (true)
                {
                    byte[] byteData = Encoding.ASCII.GetBytes("ping\x0D\x0A");
                    try
                    {
                        stream.Write(byteData, 0, byteData.GetLength(0));
                        _connected = true;
                    }
                    catch 
                    {
                        _disposeClient();
                    }
                }
            }
            else
            {
                _disposeStream();
                _disposeClient();
            }
        }

        return _connected;
    }



    internal void RaiseConnectionState(bool state)
    {
        Connected = state;
        OnConnectionChanged();
    }


    /// <summary>
    /// internal method to handle the ConnectionChanged event.
    /// </summary>
    internal void OnConnectionChanged()
    {
        ConnectionState?.Invoke(this, new EventArgs());
    }



    /// <summary>
    /// Internal method to handle raising the DataRecieved event
    /// </summary>
    /// <param name="data"></param>
    internal void OnDataRecieved(string data)
    {
        DataRecieved?.Invoke(this, new EventArgsT<string>(data));
    }



    /// <summary>
    /// Starting the internal server task to run in a new thread
    /// </summary>
    internal void Start()
    {
        if (!_stopTask)
            _stopTask = true;

        if (workTask != null && _stopTask)
        {
            try
            {
                if (workTask.IsCanceled || workTask.IsCompleted || workTask.IsFaulted)
                {
                    workTask.Dispose();
                    workTask = null;
                }
            }
            catch 
            {
                throw;
            }
        }

        if (workTask != null)
            return;

        _stopTask = false;

        TaskCancel = new CancellationTokenSource();
        CancellationToken ct = TaskCancel.Token;


        workTask = Task.Factory.StartNew(() =>
        {
            while (!this._stopTask)
            {
                if (ct.IsCancellationRequested)
                    break;

                try
                {
                    client = new TcpClient(this.ipAddress, this.port);
                    doWork();
                }
                catch 
                {
                    _disposeClient();
                    _disposeStream();
                    _stopTask = true;
                }

            }
        }, ct);
    }



    /// <summary>
    /// Set up the connection and start a loop to listen for data.
    /// </summary>
    internal void doWork()
    {

        while (!_stopTask)
        {

            try
            {
                stream = client.GetStream();
            }
            catch 
            {
                _stopTask = true;
            }


            while (!_stopTask)
            {
                try
                {
                    if (stream.CanRead)
                    {
                        byte[] myReadBuffer = new byte[1024];
                        StringBuilder msg = new StringBuilder();
                        int numberOfBytesRead = 0;
                        string tcpPackageRecieved = "";

                        do
                        {
                            do
                            {
                                try
                                {
                                    numberOfBytesRead = stream.Read(myReadBuffer, 0, myReadBuffer.Length);
                                }
                                catch 
                                {
                                }

                                tcpPackageRecieved = Encoding.UTF8.GetString(myReadBuffer, 0, numberOfBytesRead);
                                msg.AppendFormat("{0}", tcpPackageRecieved);
                            }
                            while ((this._recievedataEndCharacters == null && stream.DataAvailable) || (this._recievedataEndCharacters != null && !tcpPackageRecieved.Contains(this._recievedataEndCharacters)));
                        }
                        while (this._recievedataEndCharacters == null || (this._recievedataEndCharacters != null && !tcpPackageRecieved.Contains(this._recievedataEndCharacters)));

                        OnDataRecieved(msg.ToString());
                    }
                }
                catch 
                {
                    _stopTask = true;
                    _disposeClient();
                    _disposeStream();
                }
            }
        }
    }




    public void Send(string message)
    {
        // Convert the string data to byte data using ASCII encoding.
        byte[] byteData = Encoding.ASCII.GetBytes(message);

        try
        {
            stream.Write(byteData, 0, byteData.GetLength(0));
        }
        catch 
        {

        }
    }

}


/// <summary>
/// Class for general Event with data
/// </summary>
/// <typeparam name="T"></typeparam>
public class EventArgsT<T> : EventArgs
{
    public T Data { get; set; }

    public EventArgsT(T data)
    {
        this.Data = data;
    }
}