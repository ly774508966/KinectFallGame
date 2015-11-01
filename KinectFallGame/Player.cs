
// Player.cs

using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

using Microsoft.Kinect;

namespace KinectFallGame
{
	public enum PlayerState
	{
		None, 
		Alive, 
		Disappeared, 
		GameOver
	}

	public sealed class Player
	{
		private const double BoneSize = 0.01;
		private const double HeadSize = 0.075;
		private const double HandSize = 0.03;
		public const int InvalidPlayerId = -1;
		public const int InitialLifeCount = 10;

		private static readonly Color[] PlayerColors = {
			Colors.Gold, Colors.Red, Colors.Gray, Colors.SkyBlue,
			Colors.Green, Colors.DarkGoldenrod, Colors.DarkMagenta,
			Colors.MediumSpringGreen, Colors.Brown };
		private static int ColorIndex;

		private readonly int mId;
		private readonly Color mColor;

		private Dictionary<Bone, BoneData> mSegments = new Dictionary<Bone, BoneData>();
		private Brush mJointBrush = null;
		private Brush mBoneBrush = null;
		
		private Rect mPlayerBounds;
		private Point mPlayerCenterPosition;
		private double mPlayerScale;
		private bool mIsAlive;
		private PlayerState mPlayerState = PlayerState.None;

		private int mScore = 0;

		private DateTime mTimeLastUpdated;
		private int mLifeCount = Player.InitialLifeCount;

		private int mLevel = 0;
		public const int MaxLevel = 20;

		public int Id
		{
			get { return this.mId; }
		}

		public Dictionary<Bone, BoneData> Segments
		{
			get { return this.mSegments; }
			private set { this.mSegments = value; }
		}

		public bool IsAlive
		{
			get { return this.mIsAlive; }
			set { this.mIsAlive = value; }
		}

		public PlayerState State
		{
			get { return this.mPlayerState; }
			set { this.mPlayerState = value; }
		}

		public int Score
		{
			get { return this.mScore; }
			set { this.mScore = value; }
		}

		public DateTime TimeLastUpdated
		{
			get { return this.mTimeLastUpdated; }
			set { this.mTimeLastUpdated = value; }
		}

		public int LifeCount
		{
			get { return this.mLifeCount; }
			set { this.mLifeCount = value; }
		}

		public int Level
		{
			get { return this.mLevel; }
			set { this.mLevel = value; }
		}

		public Player(int playerId)
		{
			this.mId = playerId;

			this.mColor = Player.PlayerColors[Player.ColorIndex];
			Player.ColorIndex = (Player.ColorIndex + 1) % Player.PlayerColors.Count();

			this.mJointBrush = new SolidColorBrush(this.mColor);
			this.mBoneBrush = new SolidColorBrush(this.mColor);

			this.mTimeLastUpdated = DateTime.Now;
		}

		public void SetPlayerBounds(Rect playerBounds)
		{
			this.mPlayerBounds = playerBounds;
			this.mPlayerCenterPosition.X = (this.mPlayerBounds.Left + this.mPlayerBounds.Right) / 2;
			this.mPlayerCenterPosition.Y = (this.mPlayerBounds.Top + this.mPlayerBounds.Bottom) / 2;
			this.mPlayerScale = Math.Min(this.mPlayerBounds.Width, this.mPlayerBounds.Height / 2);
		}

		private void UpdateSegmentPosition(JointType joint1, JointType joint2, Segment segment)
		{
			Bone bone = new Bone(joint1, joint2);

			if (this.mSegments.ContainsKey(bone)) {
				BoneData boneData = this.mSegments[bone];
				boneData.UpdateSegment(segment);
				this.mSegments[bone] = boneData;
			} else {
				this.mSegments.Add(bone, new BoneData(segment));
			}
		}

		public void UpdateBonePosition(JointCollection joints, JointType joint1, JointType joint2)
		{
			// セグメントの開始位置と終了位置を設定
			Segment segment = new Segment(
				joints[joint1].Position.X * this.mPlayerScale + this.mPlayerCenterPosition.X,
				this.mPlayerCenterPosition.Y - joints[joint1].Position.Y * this.mPlayerScale,
				joints[joint2].Position.X * this.mPlayerScale + this.mPlayerCenterPosition.X,
				this.mPlayerCenterPosition.Y - joints[joint2].Position.Y * this.mPlayerScale);

			// セグメントの線の太さを設定
			segment.mRadius = Math.Max(3.0, this.mPlayerBounds.Height * Player.BoneSize) / 2.0;

			this.UpdateSegmentPosition(joint1, joint2, segment);
		}

		public void UpdateJointPosition(JointCollection joints, JointType joint)
		{
			// セグメントの開始位置と終了位置を設定
			Segment segment = new Segment(
				joints[joint].Position.X * this.mPlayerScale + this.mPlayerCenterPosition.X,
				this.mPlayerCenterPosition.Y - joints[joint].Position.Y * this.mPlayerScale);

			// セグメントの半径を設定
			segment.mRadius = this.mPlayerBounds.Height * 
				((joint == JointType.Head) ? Player.HeadSize : Player.HandSize) / 2.0;

			this.UpdateSegmentPosition(joint, joint, segment);
		}

		public void Draw(UIElementCollection children)
		{
			if (this.mIsAlive == false) {
				return;
			}

			DateTime currentTime = DateTime.Now;

			foreach (var segment in this.mSegments) {
				Segment estimatedSegment = segment.Value.GetEstimatedSegment(currentTime);

				if (estimatedSegment.IsCircle() == false) {
					// ボーンの描画
					Line line = new Line() {
						StrokeThickness = estimatedSegment.mRadius * 2.0,
						X1 = estimatedSegment.mX1,
						Y1 = estimatedSegment.mY1,
						X2 = estimatedSegment.mX2,
						Y2 = estimatedSegment.mY2,
						Stroke = this.mBoneBrush, 
						StrokeStartLineCap = PenLineCap.Round, 
						StrokeEndLineCap = PenLineCap.Round
					};

					children.Add(line);
				}
			}

			foreach (var segment in this.mSegments) {
				Segment estimatedSegment = segment.Value.GetEstimatedSegment(currentTime);

				if (estimatedSegment.IsCircle() == true) {
					// 円の描画
					Ellipse circle = new Ellipse() {
						Width = estimatedSegment.mRadius * 2.0, 
						Height = estimatedSegment.mRadius * 2.0
					};

					circle.SetValue(Canvas.LeftProperty, estimatedSegment.mX1 - estimatedSegment.mRadius);
					circle.SetValue(Canvas.TopProperty, estimatedSegment.mY1 - estimatedSegment.mRadius);
					circle.StrokeThickness = 1.0;
					circle.Stroke = this.mJointBrush;
					circle.Fill = this.mBoneBrush;

					children.Add(circle);
				}
			}

			if (DateTime.Now.Subtract(this.mTimeLastUpdated).TotalMilliseconds > 1000.0) {
				// 1.0秒以上更新されない場合はプレイヤーを削除
				this.mIsAlive = false;
				this.mPlayerState = PlayerState.Disappeared;
			}
		}
	}
}
