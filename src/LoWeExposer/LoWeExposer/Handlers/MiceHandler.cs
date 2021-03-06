﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;

namespace LoWeExposer.Handlers
{
    class MiceHandler : HandlerBase
    {
        private readonly ILineLogger _lineLogger;
        private readonly Queue<MiceState> _states;
        private MiceState _lastReadState;
        private readonly object _lockObj;

        public MiceHandler(ILineLogger lineLogger)
        {
            _lineLogger = lineLogger;
            _states = new Queue<MiceState>();
            _lockObj = new object();
        }

        public void AddState(MiceState miceState)
        {
            lock (_lockObj)
            {
                _states.Enqueue(miceState);
            }
        }

        public void ClearQueue()
        {
            lock (_lockObj)
            {
                _states.Clear();
            }
        }

        protected override void backgroundWorker_DoWork(object sender, DoWorkEventArgs e)
        {

            try
            {
                while (!_cancellationToken.IsCancellationRequested)
                {
                    _socket = _tcpListener.AcceptSocket();
                    _socket.NoDelay = true;

                    bool isInititalized = false;
                    while (!_cancellationToken.IsCancellationRequested && !_tcpListener.Pending() && _socket.Connected)
                    {
                        var opCode = new byte[4];
                        if (!ReadAllImpatient(opCode))
                            continue;

                        if (IsOperation(opCode, "MICE"))
                        {
                            WriteAll(Encoding.ASCII.GetBytes("ECIM"));
                            _lineLogger.LogLine("Socket check");
                            break;
                        }

                        if (IsOperation(opCode, "INIT"))
                        {
                            isInititalized = true;
                        }
                        else if (IsOperation(opCode, "READ"))
                        {
                            if (!isInititalized)
                                break;

                            var respData = new byte[1 + 2*4 + 1];
                            lock (_lockObj)
                            {
                                if (_states.Count == 0)
                                {
                                    if (_lastReadState != null)
                                    {
                                        respData[0] = (byte) ((_lastReadState.LeftButtonDown ? 1 : 0) +
                                                              (_lastReadState.RightButtonDown ? 2 : 0));
                                        Array.Copy(BitConverter.GetBytes(_lastReadState.X), 0, respData, 1, 4);
                                        Array.Copy(BitConverter.GetBytes(_lastReadState.Y), 0, respData, 5, 4);
                                    }
                                }
                                else
                                {
                                    var actualCurrentState = _states.Dequeue();
                                    if (_lastReadState == null)
                                    {
                                        _lastReadState = actualCurrentState;
                                    }
                                    else
                                    {
                                        while (_states.Count > 0)
                                        {
                                            var peekItem = _states.Peek();

                                            if (peekItem.Wheel != actualCurrentState.Wheel)
                                                break;

                                            if (peekItem.LeftButtonDown != actualCurrentState.LeftButtonDown ||
                                                peekItem.RightButtonDown != actualCurrentState.RightButtonDown)
                                                break;

                                            if (Math.Abs(peekItem.X - _lastReadState.X) >= 300 ||
                                                Math.Abs(peekItem.Y - _lastReadState.Y) >= 300)
                                                break;

                                            actualCurrentState = _states.Dequeue();
                                        }

                                    }

                                    respData[0] = (byte) ((actualCurrentState.LeftButtonDown ? 1 : 0) +
                                                          (actualCurrentState.RightButtonDown ? 2 : 0));

                                    Array.Copy(BitConverter.GetBytes(actualCurrentState.X), 0, respData, 1, 4);
                                    Array.Copy(BitConverter.GetBytes(actualCurrentState.Y), 0, respData, 5, 4);

                                    byte wheel = 0;
                                    if (actualCurrentState.Wheel < 0)
                                        wheel = 0xff;
                                    if (actualCurrentState.Wheel > 0)
                                        wheel = 1;
                                    respData[9] = wheel;

                                    _lastReadState = actualCurrentState;
                                }
                            }

                            WriteAll(respData);

                            _lineLogger.LogLine("Mouse state sent to agent");
                        }
                        else if (IsOperation(opCode, "CLOS"))
                        {
                            if (!isInititalized)
                                break;

                            _lineLogger.LogLine("Close");
                            break;
                        }
                    }

                    _socket.Close();
                }
            }
            catch (Exception ex)
            {
                _lineLogger.LogLine($"Exception: {ex}");
                _tcpListener.Stop();
            }
        }
    }
}
