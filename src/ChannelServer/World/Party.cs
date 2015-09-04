﻿// Copyright (c) Aura development team - Licensed under GNU GPL
// For more information, see license file in the main folder

using Aura.Channel.Network.Sending;
using Aura.Channel.World.Entities;
using Aura.Mabi.Const;
using Aura.Mabi.Network;
using Aura.Shared.Util;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Aura.Channel.World
{
	public class Party
	{
		private object _sync = new object();

		private List<Creature> _members;
		private Dictionary<int, Creature> _occupiedSlots;

		public long Id { get; private set; }

		public PartyType Type { get; private set; }
		public string Name { get; private set; }
		public string DungeonLevel { get; private set; }
		public string Info { get; private set; }
		public string Password { get; private set; }

		public int MaxSize { get; private set; }

		public bool IsOpen { get; private set; }

		public PartyFinishRule Finish { get; private set; }
		public PartyExpSharing ExpRule { get; private set; }

		public Creature Leader { get; private set; }

		public int MemberCount { get { lock (_sync) return _members.Count; } }

		public bool HasPassword { get { return !string.IsNullOrWhiteSpace(this.Password); } }

		public bool HasFreeSpace { get { return (this.MemberCount < this.MaxSize); } }

		/// <summary>
		/// Initializes party.
		/// </summary>
		private Party()
		{
			_members = new List<Creature>();
			_occupiedSlots = new Dictionary<int, Creature>();
		}

		/// <summary>
		/// Creates new party with creature as leader.
		/// </summary>
		/// <param name="creature"></param>
		public static Party Create(Creature creature, PartyType type, string name, string dungeonLevel, string info, string password, int maxSize)
		{
			var party = new Party();

			party.Id = ChannelServer.Instance.PartyManager.GetNextPartyId();

			party._members.Add(creature);
			party._occupiedSlots.Add(1, creature);
			party.Leader = creature;
			party.SetSettings(type, name, dungeonLevel, info, password, maxSize);

			creature.PartyPosition = 1;

			return party;
		}

		/// <summary>
		/// Creates new dummy party for creature.
		/// </summary>
		/// <param name="creature"></param>
		public static Party CreateDummy(Creature creature)
		{
			var party = new Party();

			party._members.Add(creature);
			party._occupiedSlots.Add(1, creature);
			party.Leader = creature;

			creature.PartyPosition = 1;

			return party;
		}

		/// <summary>
		/// Changes settings and updates clients.
		/// </summary>
		/// <param name="type"></param>
		/// <param name="name"></param>
		/// <param name="dungeonLevel"></param>
		/// <param name="info"></param>
		/// <param name="password"></param>
		/// <param name="maxSize"></param>
		public void ChangeSettings(PartyType type, string name, string dungeonLevel, string info, string password, int maxSize)
		{
			this.SetSettings(type, name, dungeonLevel, info, password, maxSize);

			Send.PartyTypeUpdate(this);

			if (this.IsOpen)
				Send.PartyMemberWantedRefresh(this);

			Send.PartySettingUpdate(this);
		}

		/// <summary>
		/// Sets given options without updating the clients.
		/// </summary>
		/// <param name="type"></param>
		/// <param name="name"></param>
		/// <param name="dungeonLevel"></param>
		/// <param name="info"></param>
		/// <param name="password"></param>
		/// <param name="maxSize"></param>
		private void SetSettings(PartyType type, string name, string dungeonLevel, string info, string password, int maxSize)
		{
			this.Type = type;
			this.Name = name;
			this.DungeonLevel = (string.IsNullOrWhiteSpace(password) ? null : password);
			this.Info = (string.IsNullOrWhiteSpace(password) ? null : password);
			this.Password = (string.IsNullOrWhiteSpace(password) ? null : password);
			this.MaxSize = Math2.Clamp(this.MemberCount, 8, maxSize);
		}

		/// <summary>
		/// Returns party member by entity id, or null if it doesn't exist.
		/// </summary>
		/// <param name="entityId"></param>
		/// <returns></returns>
		public Creature GetMember(long entityId)
		{
			lock (_sync)
				return _members.FirstOrDefault(a => a.EntityId == entityId);
		}

		/// <summary>
		/// Returns list of all members.
		/// </summary>
		/// <returns></returns>
		public Creature[] GetMembers()
		{
			lock (_sync)
				return _members.ToArray();
		}

		/// <summary>
		/// Returns list of all members, sorted by their position in the party.
		/// </summary>
		/// <returns></returns>
		public Creature[] GetSortedMembers()
		{
			lock (_sync)
				return _members.OrderBy(a => a.PartyPosition).ToArray();
		}

		/// <summary>
		/// Returns first available slot.
		/// </summary>
		/// <returns></returns>
		private int GetAvailableSlot()
		{
			for (int i = 1; i < this.MaxSize; i++)
			{
				if (!_occupiedSlots.ContainsKey(i))
					return i;
			}

			return 200;
		}

		/// <summary>
		/// Sets next leader automatically.
		/// </summary>
		/// <remarks>
		/// Official gives the character that has been created for the
		/// longest period of time precedence.
		/// </remarks>
		/// <returns></returns>
		public void AutoChooseNextLeader()
		{
			Creature newLeader;

			lock (_sync)
			{
				var time = _members[0].CreationTime;
				newLeader = _members[0];

				for (int i = 1; i < _members.Count; i++)
				{
					if (time < _members[i].CreationTime)
					{
						newLeader = _members[i];
						time = _members[i].CreationTime;
					}
				}
			}

			this.SetLeader(newLeader);
		}

		/// <summary>
		/// Sets leader to given creature, if possible.
		/// </summary>
		/// <param name="creature"></param>
		/// <returns></returns>
		public bool SetLeader(Creature creature)
		{
			lock (_sync)
			{
				if (!_members.Contains(creature))
					return false;
			}

			this.Leader = creature;
			Send.PartyChangeLeader(this);

			return true;
		}

		/// <summary>
		/// Sets leader to given entity, if possible.
		/// </summary>
		/// <param name="entitiyId"></param>
		/// <returns></returns>
		public bool SetLeader(long entitiyId)
		{
			var creature = this.GetMember(entitiyId);

			if (creature != null)
				return this.SetLeader(creature);

			return false;
		}

		/// <summary>
		/// Adds creature to party and updates the clients.
		/// </summary>
		/// <param name="creature"></param>
		/// <param name="password"></param>
		/// <returns></returns>
		public void AddMember(Creature creature, string password)
		{
			this.AddMemberSilent(creature);
			Send.PartyJoinUpdateMembers(creature);

			if (this.IsOpen)
				Send.PartyMemberWantedRefresh(this);
		}

		/// <summary>
		/// Adds creature to party without updating the clients.
		/// </summary>
		/// <param name="creature"></param>
		public void AddMemberSilent(Creature creature)
		{
			lock (_sync)
			{
				_members.Add(creature);

				creature.Party = this;
				creature.PartyPosition = this.GetAvailableSlot();

				_occupiedSlots.Add(creature.PartyPosition, creature);
			}
		}

		/// <summary>
		/// Removes creature from party if it's in it and updates the clients.
		/// </summary>
		/// <param name="creature"></param>
		public void RemoveMember(Creature creature)
		{
			this.RemoveMemberSilent(creature);

			if (this.MemberCount == 0)
			{
				this.Close();
				return;
			}

			Send.PartyLeaveUpdate(creature, this);

			if (IsOpen)
				Send.PartyMemberWantedRefresh(this);

			// What is this?
			//Send.PartyWindowUpdate(creature, party);

			if (this.Leader == creature)
			{
				this.AutoChooseNextLeader();
				this.Close();

				Send.PartyChangeLeader(this);
			}
		}

		/// <summary>
		/// Removes creature from party without updating the clients.
		/// </summary>
		/// <param name="creature"></param>
		public void RemoveMemberSilent(Creature creature)
		{
			lock (_sync)
			{
				_members.Remove(creature);
				_occupiedSlots.Remove(creature.PartyPosition);
			}

			creature.Party = Party.CreateDummy(creature);
		}

		/// <summary>
		/// Closes members wanted ad.
		/// </summary>
		public void Close()
		{
			if (!this.IsOpen)
				return;

			this.IsOpen = false;
			Send.PartyMemberWantedStateChange(this);
		}

		/// <summary>
		/// Opens members wanted ad.
		/// </summary>
		public void Open()
		{
			if (this.IsOpen)
				return;

			this.IsOpen = true;
			Send.PartyMemberWantedStateChange(this);
		}

		/// <summary>
		/// Sends the supplied packet to all members, with the option of replacing the EntityID with each member's personal ID,
		/// and excluding a specific creature.
		/// </summary>
		/// <param name="packet"></param>
		/// <param name="useMemberEntityId"></param>
		/// <param name="exclude"></param>
		public void Broadcast(Packet packet, bool useMemberEntityId = false, Creature exclude = null)
		{
			lock (_sync)
			{
				foreach (var member in _members)
				{
					if (useMemberEntityId)
						packet.Id = member.EntityId;

					if (exclude != member)
						member.Client.Send(packet);
				}
			}
		}

		/// <summary>
		/// Sets party type.
		/// </summary>
		/// <param name="type"></param>
		public void SetType(PartyType type)
		{
			if (type == this.Type)
				return;

			this.Type = type;
			Send.PartyTypeUpdate(this);
		}

		/// <summary>
		/// Sets party name.
		/// </summary>
		/// <remarks>
		/// TODO: Kinda redundant, use property?
		/// </remarks>
		/// <param name="name"></param>
		public void SetName(string name)
		{
			this.Name = name;
		}

		/// <summary>
		/// Sets dungeon level.
		/// </summary>
		/// <param name="dungeonLevel"></param>
		public void SetDungeonLevel(string dungeonLevel)
		{
			this.DungeonLevel = dungeonLevel;
		}

		/// <summary>
		/// Sets party info.
		/// </summary>
		/// <param name="info"></param>
		public void SetInfo(string info)
		{
			this.Info = info;
		}

		/// <summary>
		/// Sets party's max size.
		/// </summary>
		/// <param name="size"></param>
		public void SetSize(int size)
		{
			// TODO: Max size conf
			this.MaxSize = Math2.Clamp(this.MemberCount, 8, size);
		}

		/// <summary>
		/// Change finishing rule.
		/// </summary>
		/// <param name="rule"></param>
		public void ChangeFinish(PartyFinishRule rule)
		{
			this.Finish = rule;

			Send.PartyFinishUpdate(this);
		}

		/// <summary>
		/// Changes exp sharing rule.
		/// </summary>
		/// <param name="rule"></param>
		public void ChangeExp(PartyExpSharing rule)
		{
			this.ExpRule = rule;

			Send.PartyExpUpdate(this);
		}

		/// <summary>
		/// Sets party's password, set to empty string or null to disable.
		/// </summary>
		/// <param name="pass"></param>
		public void SetPassword(string pass)
		{
			if (string.IsNullOrWhiteSpace(pass))
				pass = null;

			this.Password = pass;

			if (this.IsOpen)
				Send.PartyMemberWantedRefresh(this);
		}

		/// <summary>
		/// Returns a list of all creatures on the altar in the same region as the leader.
		/// </summary>
		/// <returns></returns>
		public List<Creature> OnAltar()
		{
			var result = new List<Creature>();

			lock (_sync)
			{
				foreach (var member in _members.Where(a => a != this.Leader && a.RegionId == this.Leader.RegionId))
				{
					var pos = member.GetPosition();
					var clientEvent = member.Region.GetClientEvent(a => a.Data.IsAltar);

					if (clientEvent.IsInside(pos.X, pos.Y))
						result.Add(member);
				}
			}
			return result;
		}

		/// <summary>
		/// Returns which creatures in the party are both in region, and a specified range.
		/// If no range is supplied, it returns all party creatures within visual(?) range.
		/// </summary>
		/// <remarks>3000 is a total guess as to the actual visible range.</remarks>
		/// <param name="creature"></param>
		/// <param name="range">Use 0 to get every member in the region.</param>
		/// <returns></returns>
		public List<Creature> GetMembersInRange(Creature creature, int range = -1)
		{
			var result = new List<Creature>();
			var pos = creature.GetPosition();

			if (range < 0)
				range = 3000;

			lock (_sync)
			{
				foreach (var member in _members.Where(a => a != creature && a.RegionId == this.Leader.RegionId))
				{
					if (range == 0 || pos.InRange(member.GetPosition(), range))
						result.Add(member);
				}
			}

			return result;
		}

		/// <summary>
		/// Returns a list of all members in the same region as the specified creature.
		/// </summary>
		/// <param name="creature"></param>
		/// <returns></returns>
		public List<Creature> GetMembersInRegion(Creature creature)
		{
			return this.GetMembersInRange(creature, 0);
		}

		/// <summary>
		/// Returns a list of all members in the region specified.
		/// </summary>
		/// <param name="regionId"></param>
		/// <returns></returns>
		public List<Creature> GetMembersInRegion(int regionId)
		{
			var result = new List<Creature>();

			lock (_sync)
				result.AddRange(_members.Where(a => a.RegionId == regionId));

			return result;
		}

		/// <summary>
		/// Deals with removing disconnected players from the party.
		/// </summary>
		/// <param name="creature"></param>
		public void DisconnectedMember(Creature creature)
		{
			lock (_sync)
			{
				_members.Remove(creature);
				_occupiedSlots.Remove(creature.PartyPosition);
			}

			if (this.MemberCount > 0)
			{
				// Choose new leader if the old one disconnected
				if (this.Leader == creature)
				{
					this.AutoChooseNextLeader();
					this.Close();

					Send.PartyChangeLeader(this);
				}

				if (this.IsOpen)
					Send.PartyMemberWantedRefresh(this);

				Send.PartyLeaveUpdate(creature, this);
			}
		}

		/// <summary>
		/// Returns the party name in the format the Party Member Wanted
		/// functionality requires.
		/// </summary>
		public override string ToString()
		{
			return string.Format("p{0}{1:d2}{2:d2}{3}{4}", (int)this.Type, this.MemberCount, this.MaxSize, (this.HasPassword ? "y" : "n"), this.Name);
		}

		/// <summary>
		/// Returns true if password is correct or none is set.
		/// </summary>
		/// <returns></returns>
		public bool CheckPassword(string password)
		{
			if (string.IsNullOrWhiteSpace(this.Password))
				return true;

			return (password == this.Password);
		}
	}
}
