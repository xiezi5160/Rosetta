﻿using System;
using System.Collections.Generic;
using System.Net.Sockets;

namespace Alkaid
{
    public class TCPConnector : INetConnector
    {
        private TcpClient mSocket;

        // 缓冲区
        private NetStream mNetStream;
        // 临时解析
        private int tempReadPacketLength;
        private int tempReadPacketType;
        private Byte[] tempReadPacketData;

        private AsyncCallback mReadCompleteCallback;
        private AsyncCallback mSendCompleteCallback;

        private AsyncThread mSendThread = null;

        public TCPConnector(IPacketFormat packetFormat, IPacketHandlerManager packetHandlerManager) : base(packetFormat, packetHandlerManager)
        {
            mSocket = null;
            mNetStream = new NetStream(INetConnector.MAX_SOCKET_BUFFER_SIZE * 2);
            tempReadPacketLength = 0;
            tempReadPacketType = 0;
            tempReadPacketData = null;

            mReadCompleteCallback = new AsyncCallback(ReadComplete);
            mSendCompleteCallback = new AsyncCallback(SendComplete);

            mSendThread = new AsyncThread(SendLogic);
            mSendThread.Start();
        }

        public override bool Init()
        {
            base.Init();

            return true;
        }

        public override void Tick(float interval)
        {
            base.Tick(interval);

            doDecodeMessage();

            //doSendMessage(); // will do in other thread
        }

        public override void Destroy()
        {
            base.Destroy();

            mSendThread.Stop();

            DisConnect();
        }

        public override ConnectionType GetConnectionType()
        {
            return ConnectionType.TCP;
        }

        public override void Connect(string address, int port)
		{
			SetConnectStatus(ConnectionStatus.CONNECTING);
            base.Connect(address, port);
			mSocket = new TcpClient();
			AsyncThread connectThread = new AsyncThread ((thread) => {
				
				try
				{
					mSocket.Connect(mRemoteHost.GetAddress(), mRemoteHost.GetPort());
				}
				catch(Exception e)
				{
					LoggerSystem.Instance.Error(e.Message);
					SetConnectStatus(ConnectionStatus.ERROR);
					CallbackConnected(IsConnected());
					// return IsConnected();
				}

				SetConnectStatus(ConnectionStatus.CONNECTED);
				mSocket.GetStream().BeginRead(mNetStream.AsyncPipeIn, 0, INetConnector.MAX_SOCKET_BUFFER_SIZE, mReadCompleteCallback, this);

				CallbackConnected(IsConnected());
			
			});

			connectThread.Start ();

            // return IsConnected();
        }

        public override void SendPacket(IPacket packet)
        {
            Byte[] buffer = null;
            mPacketFormat.GenerateBuffer(ref buffer, packet);

            mNetStream.PushOutStream(buffer);
        }

        public override void DisConnect()
        {
            if (IsConnected())
			{
				SetConnectStatus(ConnectionStatus.DISCONNECTED);
                mSocket.GetStream().Close();
                mSocket.Close();
                mSocket = null;
                mNetStream.Clear();

                CallbackDisconnected();
            }

        }

        private void ReadComplete(IAsyncResult ar)
        {
            try
            {
                int readLength = mSocket.GetStream().EndRead(ar);
                LoggerSystem.Instance.Info("读取到数据字节数:" + readLength);
                if (readLength > 0)
                {
                    mNetStream.FinishedIn(readLength);

                    mSocket.GetStream().BeginRead(mNetStream.AsyncPipeIn, 0, INetConnector.MAX_SOCKET_BUFFER_SIZE, mReadCompleteCallback, this);
                }
                else
                {
                    // error
                    LoggerSystem.Instance.Error("读取数据为0，将要断开此链接接:" + mRemoteHost.ToString());
                    DisConnect();
                }
            }
            catch (Exception e)
            {
                LoggerSystem.Instance.Error("链接：" + mRemoteHost.ToString() + ", 发生读取错误：" + e.Message);
                DisConnect();
            }

        }

        private void SendComplete(IAsyncResult ar)
        {
            try
            {
                mSocket.GetStream().EndWrite(ar);
                int sendLength = (int)ar.AsyncState;
                LoggerSystem.Instance.Info("发送数据字节数：" + sendLength);
                if (sendLength > 0)
                {
                    mNetStream.FinishedOut(sendLength);
                }
                else
                {
                    // error
                    DisConnect();
                }
            }
            catch (Exception e)
            {
                LoggerSystem.Instance.Error("发生写入错误：" + e.Message);
                DisConnect();
            }
        }

        private void doDecodeMessage()
        {
            while (mNetStream.InStreamLength > 0 && mPacketFormat.CheckHavePacket(mNetStream.InStream, mNetStream.InStreamLength))
            {
                // 开始读取
                mPacketFormat.DecodePacket(mNetStream.InStream, ref tempReadPacketLength, ref tempReadPacketType, ref tempReadPacketData);

                mPacketHandlerManager.DispatchHandler(tempReadPacketType, tempReadPacketData);

                CallbackRecieved(tempReadPacketType, tempReadPacketData);

                // 偏移
                mNetStream.PopInStream(tempReadPacketLength);
            }
        }

        private void SendLogic(AsyncThread thread)
        {
            while (thread.IsWorking())
            {
                doSendMessage();

                System.Threading.Thread.Sleep(30);
            }
        }

        private void doSendMessage()
        {
            int length = mNetStream.OutStreamLength;
            if (IsConnected() && mNetStream.AsyncPipeOutIdle && length > 0 && mSocket.GetStream().CanWrite)
            {
                try
                {
                    mSocket.GetStream().BeginWrite(mNetStream.AsyncPipeOut, 0, length, mSendCompleteCallback, length);
                }
                catch (Exception e)
                {
                    LoggerSystem.Instance.Error("发送数据错误：" + e.Message);
                    DisConnect();
                }
            }
        }
    }
}
