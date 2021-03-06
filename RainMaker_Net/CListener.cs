﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace RainMaker_Net
{
    class CListener
    {
        // 비동기 Accepts를 위한 EventArgs
        SocketAsyncEventArgs accept_args;

        // 클라이언트의 접속을 처리할 소켓
        Socket listen_socket;

        // Accept 처리의 순서를 제어하기 위한 이벤트 변수
        AutoResetEvent flow_control_event;

        // 새로운 클라이언트가 접속했을 때 호출되는 델리게이트
        public delegate void NewClientHandler(Socket client_socket, object token);

        public NewClientHandler callback_on_newclient;

        public CListener()
        {
            this.callback_on_newclient = null;
        }

        public void start(string host, int port, int backlog)
        {
            // 소켓을 생성한다.
            this.listen_socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

            IPAddress address;

            if(host == "0.0.0.0")
            {
                address = IPAddress.Any;
            }
            else
            {
                address = IPAddress.Parse(host);
            }

            // IPEndPoint 라는 객체는 끝점이라고 말할 수 있는데 도착지점으로 이해하시면 됩니다.
            // 지점은 서버가 됩니다.서버의 IP, Port정보로 IPEndPoint가 구성됩니다.
            IPEndPoint endpoint = new IPEndPoint(address, port);

            try
            {
                // 소켓에 host 정보를 바인딩시킨 뒤 Listen 메소드를 호출하여 대기한다.
                this.listen_socket.Bind(endpoint);
                this.listen_socket.Listen(backlog);

                // accept 처리가 끝나면 호출되는 이벤트 핸들러이다.
                this.accept_args = new SocketAsyncEventArgs();
                this.accept_args.Completed += new EventHandler<SocketAsyncEventArgs>(on_aceept_completed);

                // 클라이언트가 들어오기를 기다린다.
                // 비동기 메소드이므로 블로킹되지 않고 바로 리턴되며
                // 콜백 메소드를 통해서 접속 통보를 받는다.
                //this.listen_socket.AcceptAsync(this.accept_args);

                // 특정 OS 버전에서 콘솔 입력이 대기 중일 때 accept 처리가 되지 않는 버그가 발생할 수도 있기 때문에
                // 아래와 같이 위의 동기화 작업을 처리한다.
                Thread listen_thread = new Thread(do_listen);
                listen_thread.Start();
            }
            catch(Exception e)
            {
                Console.WriteLine(e.Message);
            }
        }

        void do_listen()
        {
            // accept 처리 제어를 위해 이벤트 객체를 생성한다.
            this.flow_control_event = new AutoResetEvent(false);

            while(true)
            {
                // SocketAsyncEventArgs를 재사용하기 위해서 null로 만들어 준다.
                this.accept_args.AcceptSocket = null;

                bool pending = true;

                try
                {
                    // 비동기 Accept를 호출하여 클라이언트의 접속을 받아들인다.
                    // 비동기 메소드이지만 동기적으로 수행이 완료될 경우도 있으니
                    // 리턴 값을 확인하여 분기 처리를 해줘야 한다.
                    pending = listen_socket.AcceptAsync(this.accept_args);
                }
                catch(Exception e)
                {
                    Console.WriteLine(e.Message);
                    continue;
                }

                // 즉시 완료(리턴 값이 false일 때)가 되면
                // 이벤트가 발생하지 않으므로 콜백 메소드를 직접 호출해줘야 한다.
                // pending 상태라면 비동기 요청이 들어간 상태라는 뜻이며 콜백 메소드를 기다린다.
                if(!pending)
                {
                    on_aceept_completed(null, this.accept_args);
                }

                // 클라이언트 접속 처리가 완료되면 이벤트 객체의 신호를 전달받아 다시 루프를 수행한다.
                this.flow_control_event.WaitOne();
            }
        }

        void on_aceept_completed(object sender, SocketAsyncEventArgs e )
        {
            if(e.SocketError == SocketError.Success)
            {
                // 새로 생긴 소켓을 보관해 높은 뒤
                Socket client_socket = e.AcceptSocket;

                // 다음 연결을 받아들인다.
                // 스레드의 시작 부분에는 이벤트 객체를 생성하는 코드가 들어있습니다.
                // 하나의 접속 처리가 완료된 이후 그 다음 접속 처리를 수행하기 위해서 스레드의 흐름을 제어할
                // 필요가 있는데 그때 사용되는 이벤트 객체입니다.
                this.flow_control_event.Set();

                // 이 클래스에서는 accept까지의 역할만 수행하고 클라이언트의 접속 이후의 처리는
                // 외부로 넘기기 위해서 콜백 메소드를 호출해 주도록 한다.
                // 그 이유는 소켓 처리부와 콘텐츠 구현부를 분리하기 위해서다.
                // 콘텐츠 구현 부분은 자주 바뀔 가능성이 있지만, 소켓 Accept 부분은
                // 상대적으로 변경이 적은 부분이기 때문에 양쪽을 분리시켜 주는 것이 좋다.
                // 또한, 클래스 설계 방침에 따라 Listen에 관련된 코드만 존재하도록
                // 하기 위한 이유도 있다

                if(this.callback_on_newclient != null)
                {
                    this.callback_on_newclient(client_socket, e.UserToken);
                }

                return;
            }
            else
            {
                // Accept 실패 처리
                Console.WriteLine("Failed to accept client");
            }

            // 다음 연결을 받아 들인다.
            this.flow_control_event.Reset();
        }
    }
}
