using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;

namespace LogicBlocks.Blocks
{
    public abstract class Logic : BlockEntity, IRenderer
    {
        public enum ClientAction
        {
            Connect = 101,
            Sync = 102,
            Destroy = 103,
            Trigger = 104,
            GetState = 105,
        }

        public enum ServerState
        {
            Sync = 201,
            Remove = 202,
            ChangeState = 203
        }

        public class ServerResources(ICoreServerAPI api)
        {
            public ICoreServerAPI api = api;
            public List<Gate> connected_blocks = [];
            public List<Logic> parent_blocks = [];
        }

        protected class ClientResources(ICoreClientAPI api, MeshRef connection_false_meshref, MeshRef connection_true_meshref, MeshRef triggered_meshref, MeshRef selected_meshref, MeshRef connected_meshref, List<BlockPos> connected_coords)
        {
            public ICoreClientAPI api = api;
            public MeshRef connection_false_meshref = connection_false_meshref;
            public MeshRef connection_true_meshref = connection_true_meshref;
            public MeshRef triggered_meshref = triggered_meshref;
            public MeshRef selected_meshref = selected_meshref;
            public MeshRef connected_meshref = connected_meshref;
            public List<BlockPos> connected_coords = connected_coords;
            internal bool selected = false;
            internal float render_timer;
        }

        public List<BlockPos> connected_coords = [];
        public bool state = false;
        private BlockPos position = new(3);
        protected ServerResources? server;
        protected ClientResources? client;
        private bool placed = false;

        public double RenderOrder => 0.5;
        public int RenderRange => 24;

        public void Remove(BlockPos pos)
        {
            if (this.server == null)
                throw new InvalidOperationException();
            this.server.parent_blocks.RemoveAll(b => b.Pos == pos);
            this.server.connected_blocks.RemoveAll(b => b.Pos == pos);
            this.connected_coords.RemoveAll(b => b == pos);
        }

        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);
            tree.SetBytes("logic:connected_blocks_coords", SerializerUtil.Serialize(this.connected_coords));
            tree.SetBytes("logic:state", SerializerUtil.Serialize(this.state));
            tree.SetBytes("logic:position", SerializerUtil.Serialize(base.Pos));
        }

        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor world)
        {
            base.FromTreeAttributes(tree, world);
            this.connected_coords = SerializerUtil.Deserialize<List<BlockPos>>(
                tree.GetBytes("logic:connected_blocks_coords")
            );
            this.state = SerializerUtil.Deserialize<bool>(tree.GetBytes("logic:state"));
            this.position = SerializerUtil.Deserialize<BlockPos>(tree.GetBytes("logic:position"));
        }

        public override void OnReceivedServerPacket(int packetid, byte[] data)
        {
            if (this.client == null)
                throw new InvalidOperationException();
            base.OnReceivedServerPacket(packetid, data);
            if (packetid == (int)ServerState.Sync)
                this.client.connected_coords = SerializerUtil.Deserialize<List<BlockPos>>(data);
            else if (packetid == (int)ServerState.Remove)
            {
                var pos = SerializerUtil.Deserialize<BlockPos>(data);
                this.client.connected_coords.Remove(pos);
            }
            else if (packetid == (int)ServerState.ChangeState)
                this.state = SerializerUtil.Deserialize<bool>(data);
        }

        internal void Select()
        {
            if (this.client == null)
                throw new InvalidOperationException();
            this.client.selected = true;
        }

        internal void Unselect()
        {
            if (this.client == null)
                throw new InvalidOperationException();
            this.client.selected = false;
        }

        public void Connect(Logic logic_block)
        {
            if (this.client == null)
                throw new InvalidOperationException();
            if (logic_block.Pos == Pos)
                return;
            this.client.api.Network.SendBlockEntityPacket(Pos, (int)ClientAction.Connect, SerializerUtil.Serialize(logic_block.Pos.ToVec3i()));
        }

        public override void OnReceivedClientPacket(IPlayer fromPlayer, int packetid, byte[] data)
        {
            if (this.server == null)
                throw new InvalidOperationException();
            base.OnReceivedClientPacket(fromPlayer, packetid, data);
            if (packetid == (int)ClientAction.Connect)
            {
                var coords = SerializerUtil.Deserialize<Vec3i>(data);
                var block = this.server.api.World.BlockAccessor.GetBlockEntity(new BlockPos(coords.X, coords.Y, coords.Z));
                if (block != null)
                {
                    if (block is Gate gate_block)
                    {
                        if (gate_block.server == null)
                            throw new InvalidOperationException();
                        foreach (var connected_to_connected in gate_block.server.connected_blocks)
                            if (connected_to_connected.Pos == Pos)
                                return;

                        bool already_connected = false;
                        foreach (var connected in this.server.connected_blocks)
                            if (connected.Pos.ToVec3i() == coords)
                            {
                                already_connected = true;
                                break;
                            }
                        if (already_connected)
                        {
                            gate_block.server.parent_blocks.Remove(this);
                            this.server.connected_blocks.Remove(gate_block);
                            this.connected_coords.Remove(gate_block.Pos);
                        }
                        else
                        {
                            this.server.connected_blocks.Add(gate_block);
                            this.connected_coords.Add(gate_block.Pos);
                            gate_block.server.parent_blocks.Add(this);
                        }
                        gate_block.Refresh();
                        this.MarkDirty(true);
                        this.Sync();
                    }
                }
            }
            else if (packetid == (int)ClientAction.Sync)
            {
                this.Sync();
            }
            else if (packetid == (int)ClientAction.GetState)
                this.server.api.Network.SendBlockEntityPacket(fromPlayer as IServerPlayer, Pos, (int)ServerState.ChangeState, SerializerUtil.Serialize<bool>(this.state));
        }

        private void Sync()
        {
            if (this.server == null)
                throw new InvalidOperationException();
            var to_send = SerializerUtil.Serialize(this.connected_coords);
            foreach (IServerPlayer player in this.server.api.Server.Players)
                this.server.api.Network.SendBlockEntityPacket(player as IServerPlayer, Pos, (int)ServerState.Sync, to_send);
        }

        public override void OnPlacementBySchematic(ICoreServerAPI api, IBlockAccessor blockAccessor, BlockPos pos, Dictionary<int, Dictionary<int, int>> replaceBlocks, int centerrockblockid, Block layerBlock, bool resolveImports)
        {
            base.OnPlacementBySchematic(api, blockAccessor, pos, replaceBlocks, centerrockblockid, layerBlock, resolveImports);

            if (this.server == null)
                throw new InvalidOperationException();

            this.placed = true;
        }

        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);

            if (api is ICoreServerAPI sapi)
            {
                sapi.Event.EnqueueMainThreadTask(() =>
                {
                    if (this.server == null)
                        throw new InvalidOperationException();

                    if (this.placed)
                    {
                        var delta = base.Pos - this.position;
                        var new_connected_coords = new List<BlockPos>();
                        for (int i = 0; i < this.connected_coords.Count; ++i)
                        {
                            var coords = this.connected_coords[i];
                            coords += delta;
                            var block = this.server.api.World.BlockAccessor.GetBlockEntity(new BlockPos(coords.X, coords.Y, coords.Z));
                            if (block is not Gate gate_block)
                                continue;
                            if (!gate_block.placed)
                                continue;

                            new_connected_coords.Add(coords);
                        }
                        this.connected_coords = new_connected_coords;
                        this.server.api.Event.EnqueueMainThreadTask(() =>
                        {
                            this.placed = false;
                        }, $"resetplaced:{this.Pos}");
                    }

                    foreach (var coords in this.connected_coords)
                    {
                        var block = sapi.World.BlockAccessor.GetBlockEntity(new BlockPos(coords.X, coords.Y, coords.Z));
                        if (block is Gate logic_block)
                            this.server.connected_blocks.Add(logic_block);
                    }

                    foreach (var block in this.server.connected_blocks)
                    {
                        if (block.server == null)
                            throw new InvalidOperationException();
                        block.server.parent_blocks.Add(this);
                    }
                }, $"logicblocks:restore:{this.Pos}");
                this.position = base.Pos;
                this.server = new ServerResources(sapi);
            }
            if (api is ICoreClientAPI capi)
            {
                var system = capi.ModLoader.GetModSystem<LogicBlocksModSystem>();

                var selected_meshref = system.UploadMesh($"logicblocks:selected");
                var connection_false_meshref = system.UploadMesh($"logicblocks:connection_false");
                var connection_true_meshref = system.UploadMesh($"logicblocks:connection_true");
                var triggered_meshref = system.UploadMesh($"logicblocks:{this.Block.Code.GetName()}_triggered");
                var connected_meshref = system.UploadMesh($"logicblocks:connected");

                capi.Event.RegisterRenderer(this, EnumRenderStage.Opaque);
                capi.Network.SendBlockEntityPacket(Pos, (int)ClientAction.Sync, SerializerUtil.Serialize(""));

                this.client = new ClientResources(capi, connection_false_meshref, connection_true_meshref, triggered_meshref, selected_meshref, connected_meshref, []);
            }
        }

        private void Render(IRenderAPI rpi, Matrixf modelMat, MeshRef mesh)
        {
            if (this.client == null)
                return;

            IStandardShaderProgram prog = rpi.StandardShader;
            prog.Use();

            prog.RgbaAmbientIn = rpi.AmbientColor;
            prog.RgbaFogIn = rpi.FogColor;
            prog.FogMinIn = rpi.FogMin;
            prog.FogDensityIn = rpi.FogDensity;
            prog.RgbaLightIn = new Vec4f(1, 1, 1, 1);
            prog.RgbaGlowIn = new Vec4f(0, 0, 0, 0);

            prog.Tex2D = this.client.api.BlockTextureAtlas.AtlasTextures[1].TextureId;

            prog.ModelMatrix = modelMat.Values;
            prog.ViewMatrix = rpi.CameraMatrixOriginf;
            prog.ProjectionMatrix = rpi.CurrentProjectionMatrix;

            rpi.RenderMesh(mesh);

            prog.Stop();
        }


        public void OnRenderFrame(float delta, EnumRenderStage stage)
        {
            if (this.client == null || stage != EnumRenderStage.Opaque)
                return;

            IRenderAPI rpi = this.client.api.Render;
            Vec3d cam = this.client.api.World.Player.Entity.CameraPos;

            if (this.client.selected)
            {
                var translation = Pos.ToVec3d() + new Vec3d(0.5, 0.5, 0.5) - cam;

                this.client.render_timer += delta;
                float scale_factor = 1.001f + ((float)Math.Sin(this.client.render_timer) + 1.01f) / 10f;

                Matrixf modelMat = new Matrixf()
                    .Identity()
                    .Translate(translation.X, translation.Y, translation.Z)
                    .RotateY((float)Math.PI)
                    .Scale(scale_factor, scale_factor, scale_factor);
                this.Render(rpi, modelMat, this.client.selected_meshref);
            }

            foreach (BlockPos block in this.client.connected_coords)
            {
                var rayi = (block - Pos);
                var ray = new Vec3d(rayi.X, rayi.Y, rayi.Z);
                var dist = ray.Length();
                ray = ray.Normalize();
                var angleZ = Math.Atan2(ray.Y, Math.Sqrt(ray.X * ray.X + ray.Z * ray.Z));
                var angleY = Math.Atan2(-ray.Z, ray.X);
                var translation = Pos.ToVec3d() + new Vec3d(0.5, 0.5, 0.5) - cam + ray * dist / 2;
                Matrixf modelMat = new Matrixf()
                    .Identity()
                    .Translate(translation.X, translation.Y, translation.Z)
                    .RotateY((float)angleY + float.Pi / 2)
                    .RotateX((float)-angleZ)
                    .Scale(0.1f, 0.1f, (float)dist);


                if (this.state)
                    this.Render(rpi, modelMat, this.client.connection_true_meshref);
                else
                    this.Render(rpi, modelMat, this.client.connection_false_meshref);

                if (this.client.selected)
                {
                    translation = block.ToVec3d() + new Vec3d(0.5, 0.5, 0.5) - cam;

                    this.client.render_timer += delta;
                    float scale_factor = 1.001f + ((float)Math.Sin(this.client.render_timer) + 1.01f) / 10f;

                    modelMat = new Matrixf()
                        .Identity()
                        .Translate(translation.X, translation.Y, translation.Z)
                        .RotateY((float)Math.PI)
                        .Scale(scale_factor, scale_factor, scale_factor);
                    this.Render(rpi, modelMat, this.client.connected_meshref);
                }
            }

            if (this.state)
            {
                var translation = Pos.ToVec3d() + new Vec3d(0.5, 0.5, 0.5) - cam;

                Matrixf modelMat = new Matrixf()
                    .Identity()
                    .Translate(translation.X, translation.Y, translation.Z)
                    .RotateY((float)Math.PI)
                    .Scale(1.001f, 1.001f, 1.001f);
                this.Render(rpi, modelMat, this.client.triggered_meshref);
            }
        }

        public override void OnBlockRemoved()
        {
            base.OnBlockRemoved();
            if (this.client != null)
                this.client.api.Event.UnregisterRenderer(this, EnumRenderStage.Opaque);
            else if (this.server != null)
            {
                foreach (var connected_block in this.server.connected_blocks)
                    connected_block.Remove(this.Pos);
                foreach (var parent_block in this.server.parent_blocks)
                {
                    parent_block.Remove(this.Pos);
                    parent_block.Sync();
                }
            }
        }

        public override void OnBlockUnloaded()
        {
            base.OnBlockUnloaded();
            if (this.client != null)
                this.client.api.Event.UnregisterRenderer(this, EnumRenderStage.Opaque);
        }

        public void Dispose()
        {
            GC.SuppressFinalize(this);
        }
    }
}
