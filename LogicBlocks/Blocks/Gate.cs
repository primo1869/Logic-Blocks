using System;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Vintagestory.API.Util;

namespace LogicBlocks.Blocks
{
    public abstract class Gate: Logic
    {
        bool called = false;

        protected abstract bool Evaluate();

        public void Refresh(bool force_false = false)
        {
            if (this.server == null)
                throw new InvalidOperationException();
            if (this.called)
                return;
            this.called = true;
            var prev_state = this.state;
            if (!force_false)
                this.state = this.Evaluate();
            else
                this.state = false;
            if (prev_state != this.state)
            {
                this.MarkDirty(true);
                for (int i = 0; i < base.server.connected_blocks.Count; i++)
                    base.server.connected_blocks[i].Refresh();
                foreach (IServerPlayer player in this.server.api.Server.Players)
                    this.server.api.Network.SendBlockEntityPacket(player, Pos, (int)ServerState.ChangeState, SerializerUtil.Serialize(this.state));
            }
            this.called = false;
        }

        public override void OnReceivedClientPacket(IPlayer fromPlayer, int packetid, byte[] data)
        {
            if (this.server == null)
                throw new InvalidOperationException();
            if (packetid == (int)ClientAction.Destroy)
                this.Refresh(true);
            base.OnReceivedClientPacket(fromPlayer, packetid, data);
        }
    }
}
