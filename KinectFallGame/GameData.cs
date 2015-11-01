
// GameData.cs

using System;
using Microsoft.Kinect;

namespace KinectFallGame
{
	public enum GameMode
	{
		NoPlayer = 0,
		OnePlayer = 1, 
		TwoPlayer = 2
	}

	[Flags]
	public enum GameState
	{
		None = 0x00, 
		Title = 0x01, 
		Main = 0x02, 
		GameOver = 0x04, 
		All = 0xFF
	}

	public struct Bone
	{
		public JointType mJoint1;
		public JointType mJoint2;

		public Bone(JointType joint1, JointType joint2)
		{
			this.mJoint1 = joint1;
			this.mJoint2 = joint2;
		}
	}

	public struct Segment
	{
		public double mX1;
		public double mY1;
		public double mX2;
		public double mY2;
		public double mRadius;

		public Segment(double x1, double y1)
		{
			this.mX1 = x1;
			this.mY1 = y1;
			this.mX2 = x1;
			this.mY2 = y1;
			this.mRadius = 1.0;
		}

		public Segment(double x1, double y1, double x2, double y2)
		{
			this.mX1 = x1;
			this.mY1 = y1;
			this.mX2 = x2;
			this.mY2 = y2;
			this.mRadius = 1.0;
		}

		public bool IsCircle()
		{
			return ((this.mX1 == this.mX2) && (this.mY1 == this.mY2));
		}
	}

	public struct BoneData
	{
		public Segment mCurrentSegment;
		public Segment mLastSegment;
		public double mVelocityX1;
		public double mVelocityY1;
		public double mVelocityX2;
		public double mVelocityY2;
		public DateTime mTimeLastUpdated;

		private const double Smoothing = 0.8;

		public BoneData(Segment segment)
		{
			this.mCurrentSegment = segment;
			this.mLastSegment = segment;
			this.mVelocityX1 = 0.0;
			this.mVelocityY1 = 0.0;
			this.mVelocityX2 = 0.0;
			this.mVelocityY2 = 0.0;
			this.mTimeLastUpdated = DateTime.Now;
		}

		public void UpdateSegment(Segment segment)
		{
			this.mLastSegment = this.mCurrentSegment;
			this.mCurrentSegment = segment;

			DateTime currentTime = DateTime.Now;
			double deltaTime = currentTime.Subtract(this.mTimeLastUpdated).TotalMilliseconds;

			if (deltaTime < 10.0) {
				deltaTime = 10.0;
			}

			double currentFps = 1000.0 / deltaTime;
			this.mTimeLastUpdated = currentTime;

			if (this.mCurrentSegment.IsCircle()) {
				this.mVelocityX1 = (this.mVelocityX1 * BoneData.Smoothing) +
					(1.0 - BoneData.Smoothing) * (this.mCurrentSegment.mX1 - this.mLastSegment.mX1) * currentFps;
				this.mVelocityY1 = (this.mVelocityY1 * BoneData.Smoothing) +
					(1.0 - BoneData.Smoothing) * (this.mCurrentSegment.mY1 - this.mLastSegment.mY1) * currentFps;
			} else {
				this.mVelocityX1 = (this.mVelocityX1 * BoneData.Smoothing) +
					(1.0 - BoneData.Smoothing) * (this.mCurrentSegment.mX1 - this.mLastSegment.mX1) * currentFps;
				this.mVelocityY1 = (this.mVelocityY1 * BoneData.Smoothing) +
					(1.0 - BoneData.Smoothing) * (this.mCurrentSegment.mY1 - this.mLastSegment.mY1) * currentFps;
				this.mVelocityX2 = (this.mVelocityX2 * BoneData.Smoothing) +
					(1.0 - BoneData.Smoothing) * (this.mCurrentSegment.mX2 - this.mLastSegment.mX2) * currentFps;
				this.mVelocityY2 = (this.mVelocityY2 * BoneData.Smoothing) +
					(1.0 - BoneData.Smoothing) * (this.mCurrentSegment.mY2 - this.mLastSegment.mY2) * currentFps;
			}
		}

		public Segment GetEstimatedSegment(DateTime currentTime)
		{
			Segment estimatedSegment = this.mCurrentSegment;
			double deltaTime = currentTime.Subtract(this.mTimeLastUpdated).TotalMilliseconds;

			estimatedSegment.mX1 += this.mVelocityX1 * deltaTime / 1000.0;
			estimatedSegment.mY1 += this.mVelocityY1 * deltaTime / 1000.0;

			if (this.mCurrentSegment.IsCircle()) {
				estimatedSegment.mX2 = estimatedSegment.mX1;
				estimatedSegment.mY2 = estimatedSegment.mY1;
			} else {
				estimatedSegment.mX2 += this.mVelocityX2 * deltaTime / 1000.0;
				estimatedSegment.mY2 += this.mVelocityY2 * deltaTime / 1000.0;
			}

			return estimatedSegment;
		}
	}
}
