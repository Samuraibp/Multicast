using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Chat
{
    public partial class Form1 : Form
    {
        
        private const string MulticastGroup = "224.1.1.1";
        private const int Port = 5005;
        private const int HeartbeatIntervalMs = 3000;  
        private const int OfflineTimeoutSec = 8;        

        private UdpClient udpClient;
        private string username;
        private Dictionary<string, DateTime> onlineUsers = new Dictionary<string, DateTime>();
        private bool isRunning = true;

        public Form1()
        {
            InitializeComponent();
            InitializeChatConfiguration();
        }

        private void InitializeChatConfiguration()
        {
            username = "User_" + new Random().Next(100, 999);
            this.Text = $"Multicast Chat - {username}";

            try
            {
                udpClient = new UdpClient();
                udpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);

                IPEndPoint localEp = new IPEndPoint(IPAddress.Any, Port);
                udpClient.Client.Bind(localEp);

                IPAddress multicastIp = IPAddress.Parse(MulticastGroup);
                udpClient.JoinMulticastGroup(multicastIp);

                udpClient.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastLoopback, true);

                Task.Run(() => ReceiveMessagesAsync());
                Task.Run(() => SendHeartbeatAsync());
                Task.Run(() => CheckOnlineUsersAsync());
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка инициализации сети: {ex.Message}");
            }
        }


        private async Task ReceiveMessagesAsync()
        {
            while (isRunning)
            {
                try
                {
                    UdpReceiveResult result = await udpClient.ReceiveAsync();
                    string payload = Encoding.UTF8.GetString(result.Buffer);

                    if (payload.StartsWith("MSG:"))
                    {
                        var parts = payload.Split(new[] { ':' }, 3);
                        if (parts.Length == 3)
                        {
                            string sender = parts[1];
                            string msg = parts[2];
                            AppendToChat($"[{sender}]: {msg}");
                        }
                    }
                    else if (payload.StartsWith("ALIVE:"))
                    {
                        string sender = payload.Substring(6);

                        lock (onlineUsers)
                        {
                            onlineUsers[sender] = DateTime.Now;
                        }
                        UpdateUsersUi();
                    }
                }
                catch (ObjectDisposedException) { break; }
                catch (Exception ex) { Console.WriteLine($"Ошибка приёма: {ex.Message}"); }
            }
        }

        private async Task SendHeartbeatAsync()
        {
            byte[] buffer = Encoding.UTF8.GetBytes($"ALIVE:{username}");
            IPEndPoint remoteEp = new IPEndPoint(IPAddress.Parse(MulticastGroup), Port);

            while (isRunning)
            {
                try
                {
                    await udpClient.SendAsync(buffer, buffer.Length, remoteEp);
                }
                catch (Exception ex) { Console.WriteLine($"Ошибка heartbeat: {ex.Message}"); }

                await Task.Delay(HeartbeatIntervalMs);
            }
        }

        private async Task CheckOnlineUsersAsync()
        {
            while (isRunning)
            {
                lock (onlineUsers)
                {
                    var toRemove = onlineUsers
                        .Where(kvp => (DateTime.Now - kvp.Value).TotalSeconds > OfflineTimeoutSec)
                        .Select(kvp => kvp.Key)
                        .ToList();

                    if (toRemove.Count > 0)
                    {
                        foreach (var user in toRemove)
                        {
                            onlineUsers.Remove(user);
                        }
                        UpdateUsersUi();
                    }
                }
                await Task.Delay(1000); 
            }
        }

        private async void btnSend_Click(object sender, EventArgs e)
        {
            string msg = txtInput.Text.Trim();
            if (!string.IsNullOrEmpty(msg))
            {
                byte[] buffer = Encoding.UTF8.GetBytes($"MSG:{username}:{msg}");
                IPEndPoint remoteEp = new IPEndPoint(IPAddress.Parse(MulticastGroup), Port);

                try
                {
                    await udpClient.SendAsync(buffer, buffer.Length, remoteEp);
                    txtInput.Clear();
                }
                catch (Exception ex) { MessageBox.Show($"Ошибка отправки: {ex.Message}"); }
            }
        }

        private void txtInput_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                btnSend_Click(this, new EventArgs());
                e.SuppressKeyPress = true; 
            }
        }

        private void AppendToChat(string text)
        {
            if (txtChat.InvokeRequired)
            {
                txtChat.Invoke(new Action<string>(AppendToChat), text);
                return;
            }
            txtChat.AppendText(text + Environment.NewLine);
        }

        private void UpdateUsersUi()
        {
            if (lstUsers.InvokeRequired)
            {
                lstUsers.Invoke(new Action(UpdateUsersUi));
                return;
            }

            lock (onlineUsers)
            {
                string selected = lstUsers.SelectedItem?.ToString();

                lstUsers.Items.Clear();
                foreach (var user in onlineUsers.Keys.OrderBy(u => u))
                {
                    string displayName = (user == username) ? $"{user} (Вы)" : user;
                    lstUsers.Items.Add(displayName);
                }

                if (selected != null && lstUsers.Items.Contains(selected))
                    lstUsers.SelectedItem = selected;
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            isRunning = false;
            udpClient?.DropMulticastGroup(IPAddress.Parse(MulticastGroup));
            udpClient?.Close();
            base.OnFormClosing(e);
        }
    }
}
