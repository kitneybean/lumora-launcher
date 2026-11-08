using System.Net;
using System.Net.Sockets;
using System.Text;
using Lumora.Shared.Status;

namespace Lumora.Launcher.Tests;

public class ServerListPingTests
{
    [Fact]
    public void Parses_players_and_strips_formatting_from_motd()
    {
        var status = ServerListPing.Parse(
            """{"version":{"name":"1.21.1","protocol":767},"players":{"max":50,"online":7},"description":{"text":"§aLumora ","extra":[{"text":"Survival"}]}}""",
            12);

        Assert.True(status.IsOnline);
        Assert.Equal((7, 50, 12), (status.Players, status.MaxPlayers, status.PingMs));
        Assert.Equal("Lumora Survival", status.Motd);
    }

    [Fact]
    public async Task Talks_to_a_server_over_tcp()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var server = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync();
            var stream = client.GetStream();
            await ReadPacket(stream); // handshake
            await ReadPacket(stream); // status request
            var json = Encoding.UTF8.GetBytes("""{"players":{"max":20,"online":3},"description":"Hello"}""");
            var payload = new MemoryStream();
            payload.WriteByte(0x00);
            WriteVarInt(payload, json.Length);
            payload.Write(json);
            await WritePacket(stream, payload.ToArray());
            var ping = await ReadPacket(stream);
            await WritePacket(stream, ping); // pong echoes the payload
        });

        var status = await new ServerListPing().GetStatusAsync("127.0.0.1", port);
        await server;
        listener.Stop();

        Assert.True(status.IsOnline);
        Assert.Equal((3, 20, "Hello"), (status.Players, status.MaxPlayers, status.Motd));
    }

    [Fact]
    public async Task Closed_port_is_offline()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        var status = await new ServerListPing { Timeout = TimeSpan.FromSeconds(2) }.GetStatusAsync("127.0.0.1", port);

        Assert.False(status.IsOnline);
    }

    private static async Task<byte[]> ReadPacket(NetworkStream stream)
    {
        int length = 0, shift = 0;
        var one = new byte[1];
        while (true)
        {
            await stream.ReadExactlyAsync(one);
            length |= (one[0] & 0x7F) << shift;
            if ((one[0] & 0x80) == 0)
                break;
            shift += 7;
        }
        var data = new byte[length];
        await stream.ReadExactlyAsync(data);
        return data;
    }

    private static async Task WritePacket(NetworkStream stream, byte[] payload)
    {
        var packet = new MemoryStream();
        WriteVarInt(packet, payload.Length);
        packet.Write(payload);
        await stream.WriteAsync(packet.ToArray());
    }

    private static void WriteVarInt(Stream s, int value)
    {
        var v = (uint)value;
        do
        {
            var b = (byte)(v & 0x7F);
            v >>= 7;
            if (v != 0)
                b |= 0x80;
            s.WriteByte(b);
        } while (v != 0);
    }
}
