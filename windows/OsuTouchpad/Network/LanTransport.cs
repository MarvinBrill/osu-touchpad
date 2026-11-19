using System.Net;
using System.Net.Sockets;
using System.Text;

namespace OsuTouchpad.Network;

internal static class LanTransport
{
    public static async Task Discover(UdpClient udp, string pin, CancellationToken token)
    {
        byte[] query = Encoding.ASCII.GetBytes(Wire.Discover);
        byte[] response = Encoding.ASCII.GetBytes(Wire.Advert + pin);
        while (true)
        {
            var packet = await udp.ReceiveAsync(token);
            if (packet.Buffer.AsSpan().SequenceEqual(query))
                await udp.SendAsync(response, packet.RemoteEndPoint, token);
        }
    }

    public static async Task Positions(UdpClient udp, Action<IPAddress, byte[]> receive, CancellationToken token)
    {
        while (true)
        {
            var packet = await udp.ReceiveAsync(token);
            if (packet.Buffer.Length == Wire.SampleSize + Wire.TagSize && packet.Buffer[0] == Wire.Move)
                receive(packet.RemoteEndPoint.Address, packet.Buffer);
        }
    }
}
