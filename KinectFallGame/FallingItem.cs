
// FallingItem.cs

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using KinectFallGame.Properties;

namespace KinectFallGame
{
	[Flags]
	public enum FallingItemType : ushort
	{
		None = 0x00, 
		Coin500Yen = 0x01, 
		Coin100Yen = 0x02, 
		Coin50Yen = 0x04, 
		Coin10Yen = 0x08, 
		Coin5Yen = 0x10, 
		Coin1Yen = 0x20, 
		Cherry = 0x40, 
		BigCherry = 0x80, 
		Spike = 0x100, 
		OneUp = 0x200, 
		All = 0xFFFF
	}

	public enum FallingItemState
	{
		Falling, 
		Dissolving, 
		Remove
	}

	[Flags]
	public enum HitType : ushort
	{
		None = 0x00, 
		CoinGet = 0x01, 
		HitCherry = 0x02, 
		HitBigCherry = 0x04, 
		HitSpike = 0x08, 
		OneUp = 0x10, 
		All = 0xFFFF
	}

	public struct FallingItem
	{
		public Point mCenterPosition;
		public double mSize;
		public double mVelocityX;
		public double mVelocityY;
		public FallingItemType mType;
		public double mDissolve;
		public FallingItemState mState;
		public BitmapSource mImageSource;

		public bool Hit(Segment segment, ref Point hitCenterPosition, ref double lineHitLocation)
		{
			double minDeltaXSquared = Math.Pow(this.mSize + segment.mRadius, 2.0);

			if (segment.IsCircle()) {
				// 円とアイテムとの衝突
				if (FallingItemsManager.DistanceSquared(
					this.mCenterPosition.X, this.mCenterPosition.Y, segment.mX1, segment.mX2) <= minDeltaXSquared) {
					hitCenterPosition.X = segment.mX1;
					hitCenterPosition.Y = segment.mY1;
					lineHitLocation = 0.0;
					return true;
				}
			} else {
				// ボーンとアイテムとの衝突
				double lineLengthSquared = FallingItemsManager.DistanceSquared(
					segment.mX1, segment.mY1, segment.mX2, segment.mY2);

				if (lineLengthSquared < 0.5) {
					return (FallingItemsManager.DistanceSquared(
						this.mCenterPosition.X, this.mCenterPosition.Y, segment.mX1, segment.mY1) < minDeltaXSquared);
				}

				double u = ((this.mCenterPosition.X - segment.mX1) * (segment.mX2 - segment.mX1)) +
					(((this.mCenterPosition.Y - segment.mY1) * (segment.mY2 - segment.mY1)) / lineLengthSquared);

				if ((u >= 0.0) && (u <= 1.0)) {
					double intersectX = segment.mX1 + ((segment.mX2 - segment.mX1) * u);
					double intersectY = segment.mY1 + ((segment.mY2 - segment.mY1) * u);

					if (FallingItemsManager.DistanceSquared(
						this.mCenterPosition.X, this.mCenterPosition.Y, intersectX, intersectY) < minDeltaXSquared) {
						lineHitLocation = u;
						hitCenterPosition.X = intersectX;
						hitCenterPosition.Y = intersectY;
						return true;
					}
				} else {
					if (u < 0.0) {
						if (FallingItemsManager.DistanceSquared(
							this.mCenterPosition.X, this.mCenterPosition.Y, segment.mX1, segment.mY1) < minDeltaXSquared) {
							lineHitLocation = 0.0;
							hitCenterPosition.X = segment.mX1;
							hitCenterPosition.Y = segment.mY1;
							return true;
						}
					} else {
						if (FallingItemsManager.DistanceSquared(
						this.mCenterPosition.X, this.mCenterPosition.Y, segment.mX2, segment.mY2) < minDeltaXSquared) {
							lineHitLocation = 1.0;
							hitCenterPosition.X = segment.mX2;
							hitCenterPosition.Y = segment.mY2;
							return true;
						}
					}
				}
				return false;
			}

			return false;
		}

		public void Bound(double hitPositionX, double hitPositionY, double otherSize, double velocityX, double velocityY)
		{
			double x0 = this.mCenterPosition.X;
			double y0 = this.mCenterPosition.Y;
			double x1 = hitPositionX;
			double y1 = hitPositionY;
			double velocityX0 = this.mVelocityX - velocityX;
			double velocityY0 = this.mVelocityY - velocityY;
			double distance = this.mSize + otherSize;
			double dx = Math.Sqrt((x1 - x0) * (x1 - x0) + (y1 - y0) + (y1 - y0));
			double diffX = x1 - x0;
			double diffY = y1 - y0;
			double velocityX1 = 0.0;
			double velocityY1 = 0.0;

			x0 = x1 - diffX / dx * distance;
			y0 = y1 - diffY / dx * distance;
			diffX = x1 - x0;
			diffY = y1 - y0;

			double bSquared = Math.Pow(distance, 2.0);
			double b = distance;
			double aSquared = (velocityX0 * velocityX0) + (velocityY0 * velocityY0);
			double a = Math.Sqrt(aSquared);

			if (a > 0.000001) {
				// 物体が動いている場合は反射
				double cx = x0 + velocityX0;
				double cy = y0 + velocityY0;
				double cSquared = (x1 - cx) * (x1 - cx) + (y1 - cy) * (y1 - cy);
				double power = a * ((aSquared + bSquared - cSquared) / (2 * a * b));
				velocityX1 -= 2 * (diffX / distance * power);
				velocityY1 -= 2 * (diffY / distance * power);
			}

			this.mVelocityX += velocityX1;
			this.mVelocityY += velocityY1;
			this.mCenterPosition.X = x0;
			this.mCenterPosition.Y = y0;
		}
	}
	
	public sealed class FallingItemsManager
	{
		private const double InitialGravity = 0.017;
		private const double InitialAirFriction = 0.994;
		private const double DissolveTime = 0.4;

		private readonly Dictionary<FallingItemType, List<BitmapSource>> mImages =
			new Dictionary<FallingItemType, List<BitmapSource>>() {
				{ FallingItemType.Coin500Yen, new List<BitmapSource>() { ConvertToWPFBitmap(Resources.Coin500Yen) } },
				{ FallingItemType.Coin100Yen, new List<BitmapSource>() { ConvertToWPFBitmap(Resources.Coin100Yen) } },
				{ FallingItemType.Coin50Yen, new List<BitmapSource>() { ConvertToWPFBitmap(Resources.Coin50Yen) } },
				{ FallingItemType.Coin10Yen, new List<BitmapSource>() { ConvertToWPFBitmap(Resources.Coin10Yen) } },
				{ FallingItemType.Coin5Yen, new List<BitmapSource>() { ConvertToWPFBitmap(Resources.Coin5Yen) } },
				{ FallingItemType.Coin1Yen, new List<BitmapSource>() { ConvertToWPFBitmap(Resources.Coin1Yen) } },
				{ FallingItemType.Cherry, new List<BitmapSource>() {
					ConvertToWPFBitmap(Resources.Cherry),
					ConvertToWPFBitmap(Resources.CherryAzure),
					ConvertToWPFBitmap(Resources.CherryBlack),
					ConvertToWPFBitmap(Resources.CherryBlue),
					ConvertToWPFBitmap(Resources.CherryChartreuse),
					ConvertToWPFBitmap(Resources.CherryCyan),
					ConvertToWPFBitmap(Resources.CherryEmerald),
					ConvertToWPFBitmap(Resources.CherryGray),
					ConvertToWPFBitmap(Resources.CherryGreen),
					ConvertToWPFBitmap(Resources.CherryMagenta),
					ConvertToWPFBitmap(Resources.CherryOrange),
					ConvertToWPFBitmap(Resources.CherryPink),
					ConvertToWPFBitmap(Resources.CherryViolet),
					ConvertToWPFBitmap(Resources.CherryWhite),
					ConvertToWPFBitmap(Resources.CherryYellow) }
				}, 
                { FallingItemType.BigCherry, new List<BitmapSource>() { ConvertToWPFBitmap(Resources.CherryBig) } },
				{ FallingItemType.Spike, new List<BitmapSource>() { ConvertToWPFBitmap(Resources.SpikeDown) } },
				{ FallingItemType.OneUp, new List<BitmapSource>() { ConvertToWPFBitmap(Resources.OneUp) } }
		};

		private readonly FallingItemType[] mAllItemTypes = new FallingItemType[] {
			FallingItemType.Coin500Yen, FallingItemType.Coin100Yen,
			FallingItemType.Coin50Yen, FallingItemType.Coin10Yen,
			FallingItemType.Coin5Yen, FallingItemType.Coin1Yen,
			FallingItemType.Cherry, FallingItemType.BigCherry,
			FallingItemType.Spike, FallingItemType.OneUp
		};

		private readonly int[] mAllItemRates = new int[] {
			5, 30, 50, 60, 65, 70, 75, 95, 98, 100
		};
		
		private readonly Random mRandom = new Random();
		private readonly List<FallingItem> mFallingItems = new List<FallingItem>();
		private readonly int mMaxItemCount = 20;
		private readonly int mIntraFrameCount = 1;

		private Rect mSceneRect;

		private double mDropRate = 2.0;
		private double mItemSize = 1.0;
		private double mBaseItemSize = 20.0;

		private GameMode mGameMode = GameMode.NoPlayer;
		private FallingItemType mFilterFallingItem = FallingItemType.All;

		private double mGravity = FallingItemsManager.InitialGravity;
		private double mGravityFactor = 1.0;
		private double mAirFriction = FallingItemsManager.InitialAirFriction;

		private int mFrameCount;
		private double mTargetFrameRate = 70.0;

		private double mExpandingRate = 1.0;

		private DateTime mGameStartTime;

		public DateTime GameStartTime
		{
			get { return this.mGameStartTime; }
			private set { this.mGameStartTime = value; }
		}

		public FallingItemsManager(int maxItemCount, double frameRate, int intraFrameCount)
		{
			this.mMaxItemCount = maxItemCount;
			this.mIntraFrameCount = intraFrameCount;
			this.mTargetFrameRate = frameRate * this.mIntraFrameCount;
			this.SetGravity(this.mGravityFactor);
			this.mSceneRect.X = 0.0;
			this.mSceneRect.Y = 0.0;
			this.mSceneRect.Width = 100.0;
			this.mSceneRect.Height = 100.0;
			this.mItemSize = this.mSceneRect.Height * this.mBaseItemSize / 1000.0;
			this.mExpandingRate = Math.Exp(Math.Log(6.0) / (this.mTargetFrameRate * FallingItemsManager.DissolveTime));
		}

		public void SetFrameRate(double actualFrameRate)
		{
			this.mTargetFrameRate = actualFrameRate * this.mIntraFrameCount;
			this.mExpandingRate = Math.Exp(Math.Log(6.0) / (this.mTargetFrameRate * FallingItemsManager.DissolveTime));

			if (this.mGravityFactor != 0.0) {
				this.SetGravity(this.mGravityFactor);
			}
		}

		public void SetSceneRect(Rect rect)
		{
			this.mSceneRect = rect;
			this.mItemSize = this.mSceneRect.Height * this.mBaseItemSize / 1000.0;
		}

		public void SetDropRate(double dropRate)
		{
			this.mDropRate = dropRate;
		}

		public void SetBaseItemSize(double baseItemSize)
		{
			this.mBaseItemSize = baseItemSize;
			this.mItemSize = this.mSceneRect.Height * this.mBaseItemSize / 1000.0;
		}

		public void SetGameMode(GameMode gameMode)
		{
			this.mGameMode = gameMode;
			this.mGameStartTime = DateTime.Now;
		}

		public void SetGravity(double gravityFactor)
		{
			this.mGravityFactor = gravityFactor;
			this.mGravity = this.mGravityFactor * FallingItemsManager.InitialGravity /
				this.mTargetFrameRate / Math.Sqrt(this.mTargetFrameRate * this.mIntraFrameCount);
			this.mAirFriction = (this.mGravityFactor == 0.0) ? FallingItemsManager.InitialAirFriction : 
				Math.Exp(Math.Log(1.0 - (1.0 - FallingItemsManager.InitialAirFriction) / this.mGravityFactor) / 
					this.mIntraFrameCount);

			if (this.mGravityFactor == 0.0) {
				// 重力が0の場合は落下アイテムの動きを止める
				for (int i = 0; i < this.mFallingItems.Count; i++) {
					FallingItem fallingItem = this.mFallingItems[i];
					fallingItem.mVelocityX = 0.0;
					fallingItem.mVelocityY = 0.0;
					this.mFallingItems[i] = fallingItem;
				}
			}
		}

		public void SetFallingItemFilter(FallingItemType filterFallingItem)
		{
			this.mFilterFallingItem = filterFallingItem;
		}

		public HitType CheckHits(Player player)
		{
			int playerId = player.Id;
			Dictionary<Bone, BoneData> segments = player.Segments;

			DateTime currentTime = DateTime.Now;
			
			foreach (var pair in segments) {
				for (int i = 0; i < this.mFallingItems.Count; i++) {
					FallingItem fallingItem = this.mFallingItems[i];

					if (fallingItem.mState == FallingItemState.Falling) {
						Point hitCenterPosition = new Point(0.0, 0.0);
						double lineHitLocation = 0.0;
						Segment segment = pair.Value.GetEstimatedSegment(currentTime);

						if (fallingItem.Hit(segment, ref hitCenterPosition, ref lineHitLocation)) {
							switch (this.mGameMode) {
								case GameMode.TwoPlayer:
								case GameMode.OnePlayer:
									fallingItem.mState = FallingItemState.Dissolving;
									fallingItem.mDissolve = 0.0;
									fallingItem.mVelocityX = 0.0;
									fallingItem.mVelocityY = 0.0;
									player.Score += this.GetItemScore(fallingItem);
									
									if ((player.Score / 2000) > 0) {
										player.Level = player.Score / 2000;
									} else {
										player.Level = 1;
									}

									this.mFallingItems[i] = fallingItem;

									switch (fallingItem.mType) {
										case FallingItemType.Cherry:
										case FallingItemType.BigCherry:
										case FallingItemType.Spike:
											--player.LifeCount;

											if (player.LifeCount <= 0) {
												player.IsAlive = false;
												player.State = PlayerState.GameOver;
											}

											break;
										case FallingItemType.OneUp:
											++player.LifeCount;
											break;
									}

									switch (fallingItem.mType) {
										case FallingItemType.Cherry:
											return HitType.HitCherry;
										case FallingItemType.BigCherry:
											return HitType.HitBigCherry;
										case FallingItemType.Spike:
											return HitType.HitSpike;
										case FallingItemType.Coin500Yen:
										case FallingItemType.Coin100Yen:
										case FallingItemType.Coin50Yen:
										case FallingItemType.Coin10Yen:
										case FallingItemType.Coin5Yen:
										case FallingItemType.Coin1Yen:
											return HitType.CoinGet;
										case FallingItemType.OneUp:
											return HitType.OneUp;
									}
									return HitType.None;
							}
						}
					}
				}
			}
			return HitType.None;
		}

		public void AdvanceFrame()
		{
			for (int i = 0; i < this.mFallingItems.Count; i++) {
				FallingItem fallingItem = this.mFallingItems[i];
				fallingItem.mCenterPosition.Offset(fallingItem.mVelocityX, fallingItem.mVelocityY);
				fallingItem.mVelocityY += this.mGravity * this.mSceneRect.Height;
				fallingItem.mVelocityY *= this.mAirFriction;
				fallingItem.mVelocityX *= this.mAirFriction;
				
				// 壁と反射
				if ((fallingItem.mCenterPosition.X - fallingItem.mSize < 0.0) || 
					(fallingItem.mCenterPosition.X + fallingItem.mSize > this.mSceneRect.Right)) {
					fallingItem.mVelocityX = -fallingItem.mVelocityX;
					fallingItem.mCenterPosition.X += fallingItem.mVelocityX;
				}

				// 落下アイテムが見えなくなったら削除
				if (fallingItem.mCenterPosition.Y - fallingItem.mSize > this.mSceneRect.Bottom) {
					fallingItem.mState = FallingItemState.Remove;
				}

				// 落下アイテムが消えたら削除
				if (fallingItem.mState == FallingItemState.Dissolving) {
					fallingItem.mDissolve += 1.0 / (this.mTargetFrameRate * FallingItemsManager.DissolveTime);
					fallingItem.mSize *= this.mExpandingRate;

					if (fallingItem.mDissolve >= 1.0) {
						fallingItem.mState = FallingItemState.Remove;
					}
				}

				this.mFallingItems[i] = fallingItem;
			}

			// 落下アイテムの削除
			for (int i = 0; i < this.mFallingItems.Count; i++) {
				FallingItem fallingItem = this.mFallingItems[i];
				
				if (fallingItem.mState == FallingItemState.Remove) {
					this.mFallingItems.Remove(fallingItem);
					i--;
				}
			}

			if ((this.mFallingItems.Count < this.mMaxItemCount) && 
				(this.mRandom.NextDouble() < this.mDropRate / this.mTargetFrameRate) && 
				(this.mFilterFallingItem != FallingItemType.None)) {

				FallingItemType newItemType = FallingItemType.Cherry;
				int randomValue = 0;

				do {
					randomValue = this.mRandom.Next(101);

					for (int i = 0; i < this.mAllItemRates.Length; i++) {
						if (randomValue <= this.mAllItemRates[i]) {
							newItemType = this.mAllItemTypes[i];
							break;
						}
					}
				} while ((this.mFilterFallingItem & newItemType) == 0);

				this.DropNewItem(newItemType);
			}
		}

		public void Draw(UIElementCollection children, Dictionary<int, Player> players)
		{
			++this.mFrameCount;

			for (int i = 0; i < this.mFallingItems.Count; ++i) {
				FallingItem fallingItem = this.mFallingItems[i];

				Image image = new Image();
				
				if (fallingItem.mType == FallingItemType.Cherry) {
					bool animationFlag = (this.mFrameCount % (this.mTargetFrameRate / this.mIntraFrameCount)) >=
						(this.mTargetFrameRate / this.mIntraFrameCount / 2);
					image.Source = new CroppedBitmap(fallingItem.mImageSource, new Int32Rect(
							animationFlag ? 0 : fallingItem.mImageSource.PixelWidth / 2, 0, 
							fallingItem.mImageSource.PixelWidth / 2, 
							fallingItem.mImageSource.PixelHeight));
				} else {
					image.Source = fallingItem.mImageSource;
				}

				image.Width = fallingItem.mSize * 2;
				image.Height = fallingItem.mSize * 2;
				image.SetValue(Canvas.LeftProperty, fallingItem.mCenterPosition.X - fallingItem.mSize);
				image.SetValue(Canvas.TopProperty, fallingItem.mCenterPosition.Y - fallingItem.mSize);
				image.Opacity = (fallingItem.mState == FallingItemState.Dissolving) ?
					1.0 - Math.Pow(fallingItem.mDissolve, 2.0) : 1.0;
				image.Stretch = Stretch.Uniform;

				children.Add(image);
			}

			if (players.Count != 0) {
				for (int i = 0; i < players.Count; i++) {
					var player = players.ElementAt(i);

					if (player.Value.State == PlayerState.Alive && player.Value.LifeCount > 0) {
						Label label = FallingItemsManager.CreateLabel(
							$"[Player{player.Value.Id}: {player.Value.Score}]",
							new Rect(this.mSceneRect.Width * 0.02, this.mSceneRect.Height * (0.01 + (i * 0.10)),
								this.mSceneRect.Width * 0.8, this.mSceneRect.Height * 0.3),
							new SolidColorBrush(Colors.White));
						label.FontSize = Math.Max(1.0, Math.Min(this.mSceneRect.Width / 24.0, this.mSceneRect.Height / 24.0));
						children.Add(label);
						
						Label lifeCountLabel = FallingItemsManager.CreateLabel(
							new string('♥', player.Value.LifeCount),
							new Rect(this.mSceneRect.Width * 0.12, this.mSceneRect.Height * (0.06 + (i * 0.10)),
								this.mSceneRect.Width * 0.8, this.mSceneRect.Height * 0.3),
							new SolidColorBrush(Colors.Red));
						lifeCountLabel.FontSize = Math.Max(1.0, Math.Min(this.mSceneRect.Width / 24.0, this.mSceneRect.Height / 24.0));
						children.Add(lifeCountLabel);
					}
				}
			}

			if (this.mGameMode != GameMode.NoPlayer) {
				TimeSpan timeSpan = DateTime.Now.Subtract(this.mGameStartTime);
				string text = $"{timeSpan.Minutes}:{timeSpan.Seconds.ToString("00")}";

				Label label = FallingItemsManager.CreateLabel(text,
					new Rect(this.mSceneRect.Width * 0.1, this.mSceneRect.Height * 0.25,
						this.mSceneRect.Width * 0.9, this.mSceneRect.Height * 0.72),
					new SolidColorBrush(Colors.White));
				label.FontSize = Math.Max(1.0, this.mSceneRect.Height / 24.0);
				label.HorizontalContentAlignment = HorizontalAlignment.Right;
				label.VerticalContentAlignment = VerticalAlignment.Bottom;
				children.Add(label);
			}
		}

		private void DropNewItem(FallingItemType newItemType)
		{
			double dropWidth = Math.Min(this.mSceneRect.Right - this.mSceneRect.Left,
				this.mSceneRect.Bottom - this.mSceneRect.Top);

			double itemSize = 0.0;

			if (newItemType == FallingItemType.Cherry || 
				newItemType == FallingItemType.BigCherry || 
				newItemType == FallingItemType.Spike) {
				itemSize = this.GetItemImageSource(newItemType).PixelWidth / 2.5;
            } else {
				itemSize = this.GetItemImageSource(newItemType).PixelWidth / 8 / 2;
            }
			
			FallingItem fallingItem = new FallingItem() {
				mCenterPosition = new Point(this.mRandom.NextDouble() * dropWidth +
					(this.mSceneRect.Left + this.mSceneRect.Right - dropWidth) / 2.0,
					this.mSceneRect.Top - itemSize), 
				mSize = itemSize, 
				mVelocityX = 0.0, 
				mVelocityY = (0.5 - this.mRandom.NextDouble() - 0.25) / this.mTargetFrameRate, 
				mType = newItemType, 
				mDissolve = 0.0, 
				mState = FallingItemState.Falling, 
				mImageSource = this.GetItemImageSource(newItemType)
			};

			this.mFallingItems.Add(fallingItem);
		}

		public void Reset()
		{
			this.DissolveAllItems();
			this.mGameStartTime = DateTime.Now;
		}

		public void DissolveAllItems()
		{
			for (int i = 0; i < this.mFallingItems.Count; i++) {
				FallingItem fallingItem = this.mFallingItems[i];
				if (fallingItem.mState == FallingItemState.Falling) {
					fallingItem.mState = FallingItemState.Dissolving;
					fallingItem.mDissolve = 0.0;
					this.mFallingItems[i] = fallingItem;
				}
			}
		}

		private BitmapSource GetItemImageSource(FallingItemType itemType)
		{
			if (itemType == FallingItemType.Cherry) {
				return this.mImages[itemType][this.mRandom.Next(this.mImages[itemType].Count)];
			} else {
				return this.mImages[itemType][0];
			}
		}

		private int GetItemScore(FallingItem item)
		{
			switch (item.mType) {
				case FallingItemType.Coin500Yen:	return 500;
				case FallingItemType.Coin100Yen:	return 100;
				case FallingItemType.Coin50Yen:		return 50;
				case FallingItemType.Coin10Yen:		return 10;
				case FallingItemType.Coin5Yen:		return 5;
				case FallingItemType.Coin1Yen:		return 1;
				/*
				case FallingItemType.Cherry:		return -100;
				case FallingItemType.BigCherry:		return -500;
				case FallingItemType.Spike:			return -250;
				*/
			}
			return 0;
		}
		
		public static double DistanceSquared(double x1, double y1, double x2, double y2)
		{
			return ((x1 - x2) * (x1 - x2)) + ((y1 - y2) * (y1 - y2));
		}

		public static Label CreateLabel(string text, Rect bounds, Brush brush)
		{
			Label label = new Label() { Content = text };

			if (bounds.Width != 0.0) {
				label.SetValue(Canvas.LeftProperty, bounds.Left);
				label.SetValue(Canvas.TopProperty, bounds.Top);
				label.Width = bounds.Width;
				label.Height = bounds.Height;
			}

			label.Foreground = brush;
			label.FontFamily = new FontFamily("Meiryo UI");
			label.FontWeight = FontWeight.FromOpenTypeWeight(500);
			label.FontStyle = FontStyles.Normal;
			label.HorizontalAlignment = HorizontalAlignment.Center;
			label.VerticalAlignment = VerticalAlignment.Center;

			return label;
		}

		[DllImport("gdi32.dll", EntryPoint = "DeleteObject")]
		public static extern int DeleteObject(IntPtr hObject);
		
		public static BitmapSource ConvertToWPFBitmap(System.Drawing.Bitmap bitmap)
		{
			IntPtr bitmapHandle = bitmap.GetHbitmap();
			BitmapSource bitmapSource = null;

			try {
				bitmapSource = Imaging.CreateBitmapSourceFromHBitmap(bitmapHandle, IntPtr.Zero, Int32Rect.Empty,
					BitmapSizeOptions.FromEmptyOptions());
			} finally {
				FallingItemsManager.DeleteObject(bitmapHandle);
			}

			return bitmapSource;
		}
	}
}
