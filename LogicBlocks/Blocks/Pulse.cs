using System;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Vintagestory.API.Util;

namespace LogicBlocks.Blocks
{
    internal class Pulse : Activator
    {
        float timer = 0;
        long listener_id;

        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);
            if (this.server == null)
                return;
            this.listener_id = this.RegisterGameTickListener(dt => this.Tick(dt), 250);
        }

        public override void Interact()
        {
            if (this.client == null)
                throw new InvalidOperationException();
            if (this.state)
                return;
            this.client.api.Network.SendBlockEntityPacket(Pos, (int)ClientAction.Trigger, SerializerUtil.Serialize(""));
        }

        public override void OnReceivedClientPacket(IPlayer fromPlayer, int packetid, byte[] data)
        {
            base.OnReceivedClientPacket(fromPlayer, packetid, data);

            if (base.server == null)
                throw new InvalidOperationException();

            if (packetid == (int)ClientAction.Trigger)
            {
                this.state = true;
                this.MarkDirty(true);
                foreach (IServerPlayer player in this.server.api.Server.Players)
                    this.server.api.Network.SendBlockEntityPacket(player, Pos, (int)ServerState.ChangeState, SerializerUtil.Serialize(this.state));
                for (int i = 0; i < base.server.connected_blocks.Count; i++)
                {
                    base.server.connected_blocks[i].Refresh();
                }
            }
        }

        protected void Tick(float delta)
        {
            if (this.server == null)
                throw new InvalidOperationException();

            if (this.state)
            {
                timer += delta;
                if (timer > 1)
                {
                    timer = 0;
                    this.state = false;
                    this.MarkDirty(true);
                    foreach (IServerPlayer player in this.server.api.Server.Players)
                        this.server.api.Network.SendBlockEntityPacket(player, Pos, (int)ServerState.ChangeState, SerializerUtil.Serialize(this.state));
                    for (int i = 0; i < base.server.connected_blocks.Count; i++)
                    {
                        base.server.connected_blocks[i].Refresh();
                    }
                }
            }
        }

        public override void OnBlockRemoved()
        {
            this.UnregisterGameTickListener(this.listener_id);
            base.OnBlockRemoved();
        }

        public override void OnBlockUnloaded()
        {
            this.UnregisterGameTickListener(this.listener_id);
            base.OnBlockUnloaded();
        }
    }
}
