using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading;
using static System.Net.Mime.MediaTypeNames;

namespace CashCode.Net
{
    public enum BillValidatorCommands
    {
        ACK = 0x00, NAK = 0xFF, POLL = 0x33, RESET = 0x30, GET_STATUS = 0x31, SET_SECURITY = 0x32,
        IDENTIFICATION = 0x37, ENABLE_BILL_TYPES = 0x34, STACK = 0x35, RETURN = 0x36, HOLD = 0x38
    }

    public enum BillRecievedStatus { Accepted, Rejected };

    public enum BillCassetteStatus { Inplace, Removed };

    // Delegate for receiving banknote events
    public delegate void BillReceivedHandler(object Sender, BillReceivedEventArgs e);

    // Event delegate to control the tape
    public delegate void BillCassetteHandler(object Sender, BillCassetteEventArgs e);

    // Event delegate in the process of sending notes to the stack (You can return here)
    public delegate void BillStackingHandler(object Sender, BillStackedEventArgs e);

    public sealed class CashCodeBillValidator : IDisposable
    {
        #region Closed members

        private const int POLL_TIMEOUT = 200;    // Timeout waiting for a response from the reader
        private const int EVENT_WAIT_HANDLER_TIMEOUT = 10000; // Timeout waiting for unlocking

        private byte[] ENABLE_BILL_TYPES_WITH_ESCROW = new byte[6] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF };

        private EventWaitHandle _SynchCom;     // Variable synchronization of sending and reading data from the com port
        private List<byte> _ReceivedBytes;  // Bytes received

        private int _LastError;
        private bool _Disposed;
        private string _ComPortName;
        private bool _IsConnected;
        private int _BaudRate;
        private bool _IsPowerUp;
        private bool _IsListening;
        private bool _IsEnableBills;
        private object _Locker;

        private SerialPort _ComPort;
        private CashCodeErroList _ErrorList;

        private System.Timers.Timer _Listener;  // Bill acceptor listening timer

        bool _ReturnBill;

        BillCassetteStatus _cassettestatus = BillCassetteStatus.Inplace;
        #endregion

        #region Constructors

        public CashCodeBillValidator(string PortName, int BaudRate)
        {
            this._ErrorList = new CashCodeErroList();

            this._Disposed = false;
            this._IsEnableBills = false;
            this._ComPortName = "";
            this._Locker = new object();
            this._IsConnected = this._IsPowerUp = this._IsListening = this._ReturnBill = false;

            // From the specification:
            //      Baud Rate:	9600 bps/19200 bps (no negotiation, hardware selectable)
            //      Start bit:	1
            //      Data bit:	8 (bit 0 = LSB, bit 0 sent first)
            //      Parity:		Parity none 
            //      Stop bit:	1
            this._ComPort = new SerialPort();
            this._ComPort.PortName = this._ComPortName = PortName;
            this._ComPort.BaudRate = this._BaudRate = BaudRate;
            this._ComPort.DataBits = 8;
            this._ComPort.Parity = Parity.None;
            this._ComPort.StopBits = StopBits.One;
            this._ComPort.DataReceived += new SerialDataReceivedEventHandler(_ComPort_DataReceived);

            this._ReceivedBytes = new List<byte>();
            this._SynchCom = new EventWaitHandle(false, EventResetMode.AutoReset);

            this._Listener = new System.Timers.Timer();
            this._Listener.Interval = POLL_TIMEOUT;
            this._Listener.Enabled = false;
            this._Listener.Elapsed += new System.Timers.ElapsedEventHandler(_Listener_Elapsed);
        }

        #endregion

        #region Destructor

        // Destructor for code finalization
        ~CashCodeBillValidator() { Dispose(false); }

        // Implements interface IDisposable
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this); // Let's tell the GC not to finalize the object after calling Dispose, since it has already been released.
        }

        // Dispose (bool disposing) is performed in two scenarios
        // If disposing = true, the Dispose method is called explicitly or implicitly from user code
        // Managed and unmanaged resources can be released.
        // If disposing = false, then the method can be called runtime from the finalizer
        // In this case, only unmanaged resources can be released.
        private void Dispose(bool disposing)
        {
            // Check if Dispose was already called
            if (!this._Disposed)
            {
                // If disposing = true, free all managed and unmanaged resources.
                if (disposing)
                {
                    // Here we release the managed resources.
                    try
                    {
                        // Stop the timer if it works
                        if (this._IsListening)
                        {
                            this._Listener.Enabled = this._IsListening = false;
                        }

                        this._Listener.Dispose();

                        // Send off signal to bill acceptor
                        if (this._IsConnected)
                        {
                            this.DisableBillValidator();
                        }
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine("Exception=" + e.StackTrace);
                        Console.WriteLine("Exception message=" + e.Message);

                        bool bstop = true;
                    }
                }

                // Out the appropriate methods to release unmanaged resources
                // If disposing = false, then only the following code will be executed.
                try
                {
                    this._ComPort.Close();
                }
                catch { }

                _Disposed = true;
            }
        }

        #endregion

        #region Properties

        public bool IsConnected
        {
            get { return _IsConnected; }
        }

        #endregion

        #region Open methods

        /// <summary>
        /// Start wiretapping bill acceptor
        /// </summary>
        public void StartListening()
        {
            // If not connected
            if (!this._IsConnected)
            {
                this._LastError = 100020;
                throw new System.IO.IOException(this._ErrorList.Errors[this._LastError]);
            }

            // If there is no energy, then turn on
            if (!this._IsPowerUp) { this.PowerUpBillValidator(); }

            this._IsListening = true;
            this._Listener.Start();
        }

        /// <summary>
        /// Stop listening to the bill acceptor
        /// </summary>
        public void StopListening()
        {
            this._IsListening = false;
            this._Listener.Stop();
            this.DisableBillValidator();
        }

        /// <summary>
        /// Opening of the Com-port for work with the bill acceptor
        /// </summary>
        /// <returns></returns>
        public int ConnectBillValidator()
        {
            try
            {
                this._ComPort.Open();
                this._IsConnected = true;
            }
            catch (Exception e)
            {
                Console.WriteLine("Exception stacktrace=" + e.StackTrace);
                Console.WriteLine("Exception message=" + e.Message);
                this._IsConnected = false;
                this._LastError = 100010;
            }

            return this._LastError;
        }

        // Turn on bill acceptor
        public int PowerUpBillValidator()
        {
            List<byte> ByteResult = null;

            // If the com-port is not open
            if (!this._IsConnected)
            {
                this._LastError = 100020;
                throw new System.IO.IOException(this._ErrorList.Errors[this._LastError]);
            }

            // POWER UP
            ByteResult = this.SendCommand(BillValidatorCommands.POLL).ToList();

            // Check the result
            if (CheckPollOnError(ByteResult.ToArray()))
            {
                this.SendCommand(BillValidatorCommands.NAK);
                throw new System.ArgumentException(this._ErrorList.Errors[this._LastError]);
            }

            // Otherwise, send a confirmation signal
            this.SendCommand(BillValidatorCommands.ACK);

            // RESET
            ByteResult = this.SendCommand(BillValidatorCommands.RESET).ToList();

            //If you have not received the ACK signal from the bill acceptor
            if (ByteResult.Count > 3 && ByteResult[3] != 0x00)
            {
                this._LastError = 100050;
                return this._LastError;
            }

            // INITIALIZE
            // Then again we interrogate the bill acceptor
            ByteResult = this.SendCommand(BillValidatorCommands.POLL).ToList();

            if (CheckPollOnError(ByteResult.ToArray()))
            {
                this.SendCommand(BillValidatorCommands.NAK);
                throw new System.ArgumentException(this._ErrorList.Errors[this._LastError]);
            }

            // Otherwise, send a confirmation signal
            this.SendCommand(BillValidatorCommands.ACK);

            // GET STATUS
            ByteResult = this.SendCommand(BillValidatorCommands.GET_STATUS).ToList();

            // The GET STATUS command returns 6 bytes of response. If all are 0, then the status is ok and you can continue working, otherwise the error
            if (ByteResult.Count > 8)
            {
                if (ByteResult[3] != 0x00 || ByteResult[4] != 0x00 || ByteResult[5] != 0x00 ||
                    ByteResult[6] != 0x00 || ByteResult[7] != 0x00 || ByteResult[8] != 0x00)
                {
                    this._LastError = 100070;
                    throw new System.ArgumentException(this._ErrorList.Errors[this._LastError]);
                }
            }

            this.SendCommand(BillValidatorCommands.ACK);

            // SET_SECURITY (in the test case it sends 3 bytes (0 0 0)
            ByteResult = this.SendCommand(BillValidatorCommands.SET_SECURITY, new byte[3] { 0x00, 0x00, 0x00 }).ToList();

            //If you have not received a signal from the bill acceptor ACK
            if (ByteResult.Count > 3 && ByteResult[3] != 0x00)
            {
                this._LastError = 100050;
                return this._LastError;
            }

            // IDENTIFICATION
            ByteResult = this.SendCommand(BillValidatorCommands.IDENTIFICATION).ToList();
            this.SendCommand(BillValidatorCommands.ACK);


            // POLL
            // Then again we interrogate the bill acceptor. Must get the command INITIALIZE
            ByteResult = this.SendCommand(BillValidatorCommands.POLL).ToList();

            // Check the result
            if (CheckPollOnError(ByteResult.ToArray()))
            {
                this.SendCommand(BillValidatorCommands.NAK);
                throw new System.ArgumentException(this._ErrorList.Errors[this._LastError]);
            }

            // Otherwise, send a confirmation signal
            this.SendCommand(BillValidatorCommands.ACK);

            // POLL
            // Then again we interrogate the bill acceptor. Must receive UNIT DISABLE command
            ByteResult = this.SendCommand(BillValidatorCommands.POLL).ToList();

            // Check the result
            if (CheckPollOnError(ByteResult.ToArray()))
            {
                this.SendCommand(BillValidatorCommands.NAK);
                throw new System.ArgumentException(this._ErrorList.Errors[this._LastError]);
            }

            // Otherwise, send a confirmation signal
            this.SendCommand(BillValidatorCommands.ACK);

            this._IsPowerUp = true;

            return this._LastError;
        }

        // Enable billing mode
        public int EnableBillValidator()
        {
            List<byte> ByteResult = null;

            // If the com-port is not open
            if (!this._IsConnected)
            {
                this._LastError = 100020;
                throw new System.IO.IOException(this._ErrorList.Errors[this._LastError]);
            }

            try
            {
                if (!_IsListening)
                {
                    throw new InvalidOperationException("Error method of accepting bills. You must call the StartListening method.");
                }

                lock (_Locker)
                {
                    _IsEnableBills = true;

                    // send the ENABLE BILL TYPES command (in the test example, it sends 6 bytes (255 255 255 0 0 0) The hold function is on (Escrow)
                    ByteResult = this.SendCommand(BillValidatorCommands.ENABLE_BILL_TYPES, ENABLE_BILL_TYPES_WITH_ESCROW).ToList();

                    //If you have not received the ACK signal from the bill acceptor
                    if (ByteResult.Count > 3 && ByteResult[3] != 0x00)
                    {
                        this._LastError = 100050;
                        throw new System.ArgumentException(this._ErrorList.Errors[this._LastError]);
                    }

                    // Then again we interrogate the bill acceptor
                    ByteResult = this.SendCommand(BillValidatorCommands.POLL).ToList();

                    // Check the result
                    if (CheckPollOnError(ByteResult.ToArray()))
                    {
                        this.SendCommand(BillValidatorCommands.NAK);
                        throw new System.ArgumentException(this._ErrorList.Errors[this._LastError]);
                    }

                    // Otherwise, send a confirmation signal
                    this.SendCommand(BillValidatorCommands.ACK);
                }
            }
            catch
            {
                this._LastError = 100030;
            }

            return this._LastError;
        }

        // Turn off the reception of bills
        public int DisableBillValidator()
        {
            List<byte> ByteResult = null;

            lock (_Locker)
            {
                // If the com-port is not open
                if (!this._IsConnected)
                {
                    this._LastError = 100020;
                    throw new System.IO.IOException(this._ErrorList.Errors[this._LastError]);
                }

                _IsEnableBills = false;

                // send the ENABLE BILL TYPES command (in the test case it sends 6 bytes (0 0 0 0 0 0)
                ByteResult = this.SendCommand(BillValidatorCommands.ENABLE_BILL_TYPES, new byte[6] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }).ToList();
            }

            //If you have not received the ACK signal from the bill acceptor
            if (ByteResult.Count > 3 && ByteResult[3] != 0x00)
            {
                this._LastError = 100050;
                return this._LastError;
            }

            return this._LastError;
        }

        #endregion

        #region Closed methods

        private bool CheckPollOnError(byte[] ByteResult)
        {
            bool IsError = false;

            if (ByteResult.Length < 3)
                return IsError;

            //If you have received the third byte from the bill acceptor equal to 30Н (ILLEGAL COMMAND)
            if (ByteResult[3] == 0x30)
            {
                this._LastError = 100040;
                IsError = true;
            }
            //If you have received the third byte from the bill acceptor equal to 41Н (Drop Cassette Full)
            else if (ByteResult[3] == 0x41)
            {
                this._LastError = 100080;
                IsError = true;
            }
            // If you have received the third byte from the bill acceptor equal to 42Н (Drop Cassette out of position)
            else if (ByteResult[3] == 0x42)
            {
                this._LastError = 100070;
                IsError = true;
            }
            //If you have received the third byte from the bill acceptor equal to 43Н (Validator Jammed)
            else if (ByteResult[3] == 0x43)
            {
                this._LastError = 100090;
                IsError = true;
            }
            //If you have received the third byte from the bill acceptor equal to 44Н (Drop Cassette Jammed)
            else if (ByteResult[3] == 0x44)
            {
                this._LastError = 100100;
                IsError = true;
            }
            //If you have received the third byte from the bill acceptor equal to 45Н (Cheated)
            else if (ByteResult[3] == 0x45)
            {
                this._LastError = 100110;
                IsError = true;
            }
            //If you have received the third byte from the bill acceptor equal to 46Н (Pause)
            else if (ByteResult[3] == 0x46)
            {
                this._LastError = 100120;
                IsError = true;
            }
            //If you have received the third byte from the bill acceptor equal to 47Н (Generic Failure codes)
            else if (ByteResult[3] == 0x47)
            {
                if (ByteResult[4] == 0x50) { this._LastError = 100130; }          // Stack Motor Failure
                else if (ByteResult[4] == 0x51) { this._LastError = 100140; }   // Transport Motor Speed Failure
                else if (ByteResult[4] == 0x52) { this._LastError = 100150; }   // Transport Motor Failure
                else if (ByteResult[4] == 0x53) { this._LastError = 100160; }   // Aligning Motor Failure
                else if (ByteResult[4] == 0x54) { this._LastError = 100170; }   // Initial Cassette Status Failure
                else if (ByteResult[4] == 0x55) { this._LastError = 100180; }   // Optic Canal Failure
                else if (ByteResult[4] == 0x56) { this._LastError = 100190; }   // Magnetic Canal Failure
                else if (ByteResult[4] == 0x5F) { this._LastError = 100200; }   // Capacitance Canal Failure
                IsError = true;
            }

            return IsError;
        }



        // Sending a command to a bill acceptor
        private byte[] SendCommand(BillValidatorCommands cmd, byte[] Data = null)
        {
            if (cmd == BillValidatorCommands.ACK || cmd == BillValidatorCommands.NAK)
            {
                byte[] bytes = null;

                if (cmd == BillValidatorCommands.ACK) { bytes = Package.CreateResponse(ResponseType.ACK); }
                if (cmd == BillValidatorCommands.NAK) { bytes = Package.CreateResponse(ResponseType.NAK); }

                if (bytes != null) { this._ComPort.Write(bytes, 0, bytes.Length); }

                return null;
            }
            else
            {
                Package package = new Package();
                package.Cmd = (byte)cmd;

                if (Data != null) { package.Data = Data; }

                byte[] CmdBytes = package.GetBytes();

                // Log sended bytes to debugger
                string strCmdBytes = "";
                foreach (var b in CmdBytes)
                {
                    strCmdBytes += b.ToString("X") + ", ";
                }
                Debug.WriteLine("Sended command: " + strCmdBytes);
                // --------------------------------

                /*byte[] temp = new byte[6];
                for (int i = 0; i < 6; i++)
                {
                    temp[i] = CmdBytes[i];
                }

                this._ComPort.Write(temp, 0, 6);*/
                this._ComPort.Write(CmdBytes, 0, CmdBytes.Length);

                // Let's wait while we receive data from a com-port
                this._SynchCom.WaitOne(EVENT_WAIT_HANDLER_TIMEOUT);
                this._SynchCom.Reset();

                byte[] ByteResult = this._ReceivedBytes.ToArray();

                // Log recieved bytes to debugger
                string strByteResult = "";
                foreach (var b in ByteResult)
                {
                    strByteResult += b.ToString("X") + ", ";
                }
                Debug.WriteLine("Recieved command: " + strByteResult);
                // --------------------------------

                // If CRC is OK, then check the fourth bit with the result
                // Must already get data from the com-port, so check the CRC

                //if (ByteResult.Length == 0 || !Package.CheckCRC(ByteResult))
                if (ByteResult.Length == 0)
                {
                    throw new ArgumentException("Mismatch of the checksum of the received message. The device may not be connected to the COM port. Check connection settings");
                }

                return ByteResult;
            }

        }

        // Table of currency codes
        private int CashCodeTable(byte code)
        {
            int result = 0;

            /*if (code == 0x02) { result = 10; }             // 10 р.
            else if (code == 0x03) { result = 50; }      // 50 р.
            else if (code == 0x04) { result = 100; }    // 100 р.
            else if (code == 0x0c) { result = 200; }    // 200 р.
            else if (code == 0x05) { result = 500; }    // 500 р.
            else if (code == 0x06) { result = 1000; }  // 1000 р.
            else if (code == 0x0d) { result = 2000; }  // 2000 р.
            else if (code == 0x07) { result = 5000; }  // 5000 р.*/

            if (code == 0x02) { result = 5; }             // €5.
            else if (code == 0x03) { result = 10; }    // €10
            else if (code == 0x04) { result = 20; }    // €20
            else if (code == 0x05) { result = 50; }    // €50
            else if (code == 0x06) { result = 100; }  // €100
            else if (code == 0x07) { result = 200; }  // €200
            else if (code == 0x08) { result = 500; }  // €500

            return result;
        }

        #endregion

        #region Events

        /// <summary>
        /// Event receiving bills
        /// </summary>

        public event BillReceivedHandler BillReceived;

        private void OnBillReceived(BillReceivedEventArgs e)
        {
            if (BillReceived != null)
            {
                BillReceived(this, new BillReceivedEventArgs(e.Status, e.Value, e.RejectedReason));
            }
        }

        public event BillCassetteHandler BillCassetteStatusEvent;
        private void OnBillCassetteStatus(BillCassetteEventArgs e)
        {
            if (BillCassetteStatusEvent != null)
            {
                BillCassetteStatusEvent(this, new BillCassetteEventArgs(e.Status));
            }
        }


        /// <summary>
        /// Event of the process of sending notes to the stack (Here you can return)
        /// </summary>
        public event BillStackingHandler BillStacking;

        private void OnBillStacking(BillStackedEventArgs e)
        {
            if (BillStacking != null)
            {
                bool cancel = false;
                foreach (BillStackingHandler subscriber in BillStacking.GetInvocationList())
                {
                    subscriber(this, e);

                    if (e.Cancel)
                    {
                        cancel = true;
                        break;
                    }
                }

                _ReturnBill = cancel;
            }
        }

        #endregion

        #region Event handlers

        // Getting data from com-port
        private void _ComPort_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            // Let us fall asleep for 100 ms in order to give the program all the data from the com-port
            Thread.Sleep(100);
            this._ReceivedBytes.Clear();
            
            //Read bytes
            while (_ComPort.BytesToRead > 0)
            {
                this._ReceivedBytes.Add((byte)_ComPort.ReadByte());
            }
            
            // Remove the lock
            this._SynchCom.Set();
        }

        // Timer of wiretapping bill acceptor
        private void _Listener_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            this._Listener.Stop();

            try
            {
                lock (_Locker)
                {
                    List<byte> ByteResult = null;

                    // send a POLL command
                    ByteResult = this.SendCommand(BillValidatorCommands.POLL).ToList();

                    // If the fourth bit is not Idling (idle), then go ahead
                    if (ByteResult[3] != 0x14)
                    {
                        // ACCEPTING
                        //If your recieved answer 15H (Accepting)
                        if (ByteResult[3] == 0x15)
                        {
                            // We confirm
                            this.SendCommand(BillValidatorCommands.ACK);
                        }

                        // ESCROW POSITION  
                        // If the fourth bit is 1Сh (Rejecting), then the bill acceptor did not recognize the bill
                        else if (ByteResult[3] == 0x1C)
                        {
                            // Accept some kind of bill
                            this.SendCommand(BillValidatorCommands.ACK);

                            OnBillReceived(new BillReceivedEventArgs(BillRecievedStatus.Rejected, 0, this._ErrorList.Errors[ByteResult[4]]));
                        }

                        // ESCROW POSITION
                        // bill recognized
                        else if (ByteResult[3] == 0x80)
                        {
                            // Welcome
                            this.SendCommand(BillValidatorCommands.ACK);

                            // The event that the bill in the process of sending to the stack
                            OnBillStacking(new BillStackedEventArgs(CashCodeTable(ByteResult[4])));

                            // If the program responds with a return, then the return
                            if (this._ReturnBill)
                            {
                                // RETURN
                                // If the program refuses to accept the bill, send RETURN
                                ByteResult = this.SendCommand(BillValidatorCommands.RETURN).ToList();
                                this._ReturnBill = false;
                            }
                            else
                            {
                                // STACK
                                // If you have recognized, send the bill to the stack (STACK)
                                ByteResult = this.SendCommand(BillValidatorCommands.STACK).ToList();
                            }
                        }

                        // STACKING
                        // If the fourth bit is 17h, hence the process of sending the note to the stack is going on (STACKING)
                        else if (ByteResult[3] == 0x17)
                        {
                            this.SendCommand(BillValidatorCommands.ACK);
                        }

                        // Bill stacked
                        // If the fourth bit is 81h, therefore, the bill hit the stack
                        else if (ByteResult[3] == 0x81)
                        {
                            // Welcome
                            this.SendCommand(BillValidatorCommands.ACK);

                            OnBillReceived(new BillReceivedEventArgs(BillRecievedStatus.Accepted, CashCodeTable(ByteResult[4]), ""));
                        }

                        // RETURNING
                        // If the fourth bit is 18h, hence the return process is in progress.
                        else if (ByteResult[3] == 0x18)
                        {
                            this.SendCommand(BillValidatorCommands.ACK);
                        }

                        // BILL RETURNING
                        // If the fourth bit is 82h, hence the bill is returned.
                        else if (ByteResult[3] == 0x82)
                        {
                            this.SendCommand(BillValidatorCommands.ACK);
                        }

                        // Drop Cassette out of position
                        // Banknote withdrawn
                        else if (ByteResult[3] == 0x42)
                        {
                            if (_cassettestatus != BillCassetteStatus.Removed)
                            {
                                // fire event
                                _cassettestatus = BillCassetteStatus.Removed;
                                OnBillCassetteStatus(new BillCassetteEventArgs(_cassettestatus));

                            }
                        }

                        // Initialize
                        // The cassette is inserted back into place.
                        else if (ByteResult[3] == 0x13)
                        {
                            if (_cassettestatus == BillCassetteStatus.Removed)
                            {
                                // fire event
                                _cassettestatus = BillCassetteStatus.Inplace;
                                OnBillCassetteStatus(new BillCassetteEventArgs(_cassettestatus));
                            }
                        }
                    }
                }
            }
            catch (Exception)
            { }
            finally
            {
                // If the timer is off, then run
                if (!this._Listener.Enabled && this._IsListening)
                    this._Listener.Start();
            }

        }

        #endregion
    }

    /// <summary>
    /// Argument class of the event of receiving a bill in a bill acceptor
    /// </summary>
    public class BillReceivedEventArgs : EventArgs
    {

        public BillRecievedStatus Status { get; private set; }
        public int Value { get; private set; }
        public string RejectedReason { get; private set; }

        public BillReceivedEventArgs(BillRecievedStatus status, int value, string rejectedReason)
        {
            this.Status = status;
            this.Value = value;
            this.RejectedReason = rejectedReason;
        }
    }

    public class BillCassetteEventArgs : EventArgs
    {

        public BillCassetteStatus Status { get; private set; }

        public BillCassetteEventArgs(BillCassetteStatus status)
        {
            this.Status = status;
        }
    }

    public class BillStackedEventArgs : CancelEventArgs
    {
        public int Value { get; private set; }

        public BillStackedEventArgs(int value)
        {
            this.Value = value;
            this.Cancel = false;
        }
    }
}

