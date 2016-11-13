﻿// Copyright (c) Cragon. All rights reserved.

namespace GameCloud.Orleans.Gateway
{
    using System;
    using System.Collections.Generic;
    using System.Collections.Concurrent;
    using System.Net;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using DotNetty.Buffers;
    using DotNetty.Transport.Channels;
    using GameCloud.Unity.Common;

    public class GatewayChannelHandler : ChannelHandlerAdapter
    {
        //---------------------------------------------------------------------
        private GatewaySessionFactory factory;
        private ConcurrentDictionary<IChannelHandlerContext, GatewaySession> mapSession
            = new ConcurrentDictionary<IChannelHandlerContext, GatewaySession>();
        private System.Timers.Timer timer;

        //---------------------------------------------------------------------
        public GatewayChannelHandler(GatewaySessionFactory factory)
        {
            this.factory = factory;

            timer = new System.Timers.Timer();
            timer.Interval = 3000;
            timer.Elapsed += (obj, evt) =>
            {
                //var t = obj as System.Timers.Timer;

                int count = mapSession.Count;
                string title = Gateway.Instance.ConsoleTitle;
                string version = Gateway.Instance.Version;
                Console.Title = string.Format("{0} {1}, ConnectionCount={2}", title, version, count);
            };
            timer.Start();
        }

        //---------------------------------------------------------------------
        public override void ChannelActive(IChannelHandlerContext context)
        {
            var session = (GatewaySession)this.factory.createRpcSession(null);
            mapSession[context] = session;

            session.ChannelActive(context);
        }

        //---------------------------------------------------------------------
        public override void ChannelInactive(IChannelHandlerContext context)
        {
            GatewaySession session = null;
            mapSession.TryRemove(context, out session);

            if (session != null)
            {
                session.ChannelInactive(context);
            }
        }

        //---------------------------------------------------------------------
        public override void ChannelRegistered(IChannelHandlerContext context)
        {
        }

        //---------------------------------------------------------------------
        public override void ChannelUnregistered(IChannelHandlerContext context)
        {
        }

        //---------------------------------------------------------------------
        public override void ChannelRead(IChannelHandlerContext context, object message)
        {
            var msg = message as IByteBuffer;
            msg.WithOrder(ByteOrder.BigEndian);

            GatewaySession session = null;
            if (mapSession.TryGetValue(context, out session))
            {
                session.onRecvData(msg.ToArray());
            }
        }

        //---------------------------------------------------------------------
        public override void ChannelReadComplete(IChannelHandlerContext context)
        {
            context.Flush();
        }

        //---------------------------------------------------------------------
        public override void ExceptionCaught(IChannelHandlerContext context, Exception exception)
        {
            if (exception is System.ObjectDisposedException)
            {
                // do nothting
            }
            else
            {
                Console.WriteLine("Exception: \n" + exception);
            }

            context.CloseAsync();
        }
    }
}
