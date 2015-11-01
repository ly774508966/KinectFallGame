
// MainWindow.xaml.cs

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Kinect;
using Microsoft.Kinect.Toolkit;

using KinectFallGame.Properties;

namespace KinectFallGame
{
	using System.Diagnostics;
	using System.Media;
	using System.Windows.Controls;
	using MMRESULT = System.UInt32;
	using UINT = System.UInt32;

	public partial class MainWindow : Window
	{
		private const int TimerResolution = 2;
		private const int MaxFrameRate = 70;
		private const int MinFrameRate = 15;
		private const int MaxItemCount = 80;
		private const int IntraFrameCount = 3;
		private const double InitialGravity = 1.0;
		private const double InitialItemSize = 32.0;
		private const double InitialDropRate = 2.0;

		private Thread mGameThread = null;
		private bool mIsGameThreadRunning = false;

		private KinectSensorChooser mKinectSensorChooser = null;
		private KinectSensor mCurrentKinectSensor = null;
		// private SpeechRecognitionEngine mSpeechRecognitionEngine = null;

		private byte[] mPixelBuffer = null;
		private Skeleton[] mSkeletonBuffer = null;
		private WriteableBitmap mMainImageBuffer = null;
		
		private Dictionary<int, Player> mPlayers = new Dictionary<int, Player>();
		private int mAlivePlayersCount = 0;
		private List<KeyValuePair<int, TimeSpan>> mHighScores = new List<KeyValuePair<int, TimeSpan>>();

		private Rect mScreenRect;
		private Rect mPlayerBounds;
		private Rect mFallingItemBounds;
		
		private DateTime mTimeLastFrame = DateTime.MinValue;
		private DateTime mTimeNextFrame = DateTime.MinValue;
		private double mTargetFrameRate = MainWindow.MaxFrameRate;
		private double mCurrentFrameRate = 0.0;
		private double mActualFrameTime;
		private int mFrameCount = 0;

		private double mGravity = MainWindow.InitialGravity;
		private double mItemSize = MainWindow.InitialItemSize;
		private double mDropRate = MainWindow.InitialDropRate;

		private FallingItemsManager mFallingItemsManager;

		private MediaPlayer mBGMPlayer = new MediaPlayer();
		private SoundPlayer mSoundCoinGet = new SoundPlayer();
		private SoundPlayer mSoundHit = new SoundPlayer();
		private SoundPlayer mSoundOneUp = new SoundPlayer();

		private GameState mGameState = GameState.Title;

		private const int mGameOverTime = 500;
		private DateTime mGameOverStartTime = DateTime.MinValue;
		private Player mLastGameOverPlayer = null;

		public KinectSensor CurrentKinectSensor
		{
			get { return this.mCurrentKinectSensor; }
			private set { this.mCurrentKinectSensor = value; }
		}

		public GameState State
		{
			get { return this.mGameState; }
			set { this.mGameState = value; }
		}

		public Dictionary<int, Player> Players
		{
			get { return this.mPlayers; }
			private set { this.mPlayers = value; }
		}

		public List<KeyValuePair<int, TimeSpan>> HighScores
		{
			get { return this.mHighScores; }
			private set { this.mHighScores = value; }
		}
		
		[DllImport("Winmm.dll", EntryPoint = "timeBeginPeriod")]
		private static extern MMRESULT timeBeginPeriod(UINT uPeriod);

		[DllImport("Winmm.dll", EntryPoint = "timeEndPeriod")]
		private static extern MMRESULT timeEndPeriod(UINT uPeriod);

		public MainWindow()
		{
			this.InitializeComponent();
		}

		private void OnWindowLoaded(object sender, RoutedEventArgs eventArgs)
		{
			this.mKinectSensorChooser = new KinectSensorChooser();
			this.mKinectSensorChooser.KinectChanged += this.OnKinectChanged;
			this.mKinectSensorChooser.Start();
			
			this.mFallingItemsManager = new FallingItemsManager(
				MainWindow.MaxItemCount, this.mTargetFrameRate, MainWindow.IntraFrameCount);

			this.UpdatePlayFieldSize();

			this.mFallingItemsManager.SetGravity(this.mGravity);
			this.mFallingItemsManager.SetDropRate(this.mDropRate);
			this.mFallingItemsManager.SetBaseItemSize(this.mItemSize);
			this.mFallingItemsManager.SetFallingItemFilter(FallingItemType.All);
			this.mFallingItemsManager.SetGameMode(GameMode.NoPlayer);

			this.mBGMPlayer.Open(new Uri("./Resources/WindowsXPWelcomeMusic.wav", UriKind.Relative));
			this.mBGMPlayer.MediaEnded += this.OnBGMEnded;
			this.mBGMPlayer.Play();

			this.mSoundCoinGet.Stream = Properties.Resources.CoinGet;
			this.mSoundHit.Stream = Properties.Resources.Hit;
			this.mSoundOneUp.Stream = Properties.Resources.OneUpSound;

			MainWindow.timeBeginPeriod(MainWindow.TimerResolution);

			this.mGameThread = new Thread(new ThreadStart(this.GameThread));
			this.mGameThread.SetApartmentState(ApartmentState.STA);
			this.mGameThread.Start();
		}
		
		private void OnWindowClosed(object sender, EventArgs eventArgs)
		{
			MainWindow.timeEndPeriod(MainWindow.TimerResolution);

			this.mIsGameThreadRunning = false;
		}

		private void OnWindowClosing(object sender, CancelEventArgs eventArgs)
		{
			this.mKinectSensorChooser.Stop();

			this.mIsGameThreadRunning = false;
		}

		private void OnKinectChanged(object sender, KinectChangedEventArgs eventArgs)
		{
			if (eventArgs.OldSensor != null) {
				this.UninitializeKinect(eventArgs.OldSensor);
			}

			if (eventArgs.NewSensor != null) {
				this.InitializeKinect(eventArgs.NewSensor);
			}
		}

		private void InitializeKinect(KinectSensor kinectSensor)
		{
			kinectSensor.ColorStream.Enable(ColorImageFormat.RgbResolution640x480Fps30);

			TransformSmoothParameters transformSmoothParameters = new TransformSmoothParameters() {
				Correction = 0.5f, 
				JitterRadius = 0.05f, 
				MaxDeviationRadius = 0.04f, 
				Prediction = 0.5f, 
				Smoothing = 0.5f
			};

			kinectSensor.SkeletonStream.Enable(transformSmoothParameters);

			this.mPixelBuffer = new byte[kinectSensor.ColorStream.FramePixelDataLength];
			this.mSkeletonBuffer = new Skeleton[kinectSensor.SkeletonStream.FrameSkeletonArrayLength];
			this.mMainImageBuffer = new WriteableBitmap(
				kinectSensor.ColorStream.FrameWidth, kinectSensor.ColorStream.FrameHeight,
				96.0, 96.0, PixelFormats.Bgr32, null);
			this.mMainImage.Source = this.mMainImageBuffer;
			this.mMainImage.Stretch = Stretch.Uniform;

			kinectSensor.AllFramesReady += this.OnAllFramesReady;

			this.mCurrentKinectSensor = kinectSensor;
		}

		private void OnAllFramesReady(object sender, AllFramesReadyEventArgs eventArgs)
		{
			KinectSensor kinectSensor = sender as KinectSensor;

			if (kinectSensor == null) {
				return;
			}

			using (SkeletonFrame skeletonFrame = eventArgs.OpenSkeletonFrame()) {
				if (skeletonFrame != null) {
					if ((this.mSkeletonBuffer == null) || 
						(this.mSkeletonBuffer.Length != skeletonFrame.SkeletonArrayLength)) {
						this.mSkeletonBuffer = new Skeleton[skeletonFrame.SkeletonArrayLength];
					}

					skeletonFrame.CopySkeletonDataTo(this.mSkeletonBuffer);

					int playerId = 0;

					foreach (Skeleton skeleton in this.mSkeletonBuffer) {
						if (skeleton.TrackingState != SkeletonTrackingState.Tracked) {
							continue;
						}

						Player player = null;

						if (this.mPlayers.ContainsKey(playerId)) {
							player = this.mPlayers[playerId];
						} else {
							player = new Player(playerId);
							player.SetPlayerBounds(this.mPlayerBounds);
							this.mPlayers.Add(playerId, player);
						}

						player.TimeLastUpdated = DateTime.Now;

						if (skeleton.Joints.Count > 0) {
							player.IsAlive = true;
							player.State = PlayerState.Alive;

							// 衝突判定を行う部位
							// 頭, 手, 足
							player.UpdateJointPosition(skeleton.Joints, JointType.Head);
							player.UpdateJointPosition(skeleton.Joints, JointType.HandLeft);
							player.UpdateJointPosition(skeleton.Joints, JointType.HandRight);
							player.UpdateJointPosition(skeleton.Joints, JointType.FootLeft);
							player.UpdateJointPosition(skeleton.Joints, JointType.FootRight);

							// ボーン
							// 手, 肩
							player.UpdateBonePosition(skeleton.Joints, JointType.HandRight, JointType.WristRight);
							player.UpdateBonePosition(skeleton.Joints, JointType.WristRight, JointType.ElbowRight);
							player.UpdateBonePosition(skeleton.Joints, JointType.ElbowRight, JointType.ShoulderRight);

							player.UpdateBonePosition(skeleton.Joints, JointType.HandLeft, JointType.WristLeft);
							player.UpdateBonePosition(skeleton.Joints, JointType.WristLeft, JointType.ElbowLeft);
							player.UpdateBonePosition(skeleton.Joints, JointType.ElbowLeft, JointType.ShoulderLeft);

							// 頭, 肩
							player.UpdateBonePosition(skeleton.Joints, JointType.ShoulderCenter, JointType.Head);
							player.UpdateBonePosition(skeleton.Joints, JointType.ShoulderLeft, JointType.ShoulderCenter);
							player.UpdateBonePosition(skeleton.Joints, JointType.ShoulderCenter, JointType.ShoulderRight);
							
							// 足
							player.UpdateBonePosition(skeleton.Joints, JointType.HipLeft, JointType.KneeLeft);
							player.UpdateBonePosition(skeleton.Joints, JointType.KneeLeft, JointType.AnkleLeft);
							player.UpdateBonePosition(skeleton.Joints, JointType.AnkleLeft, JointType.FootLeft);

							player.UpdateBonePosition(skeleton.Joints, JointType.HipRight, JointType.KneeRight);
							player.UpdateBonePosition(skeleton.Joints, JointType.KneeRight, JointType.AnkleRight);
							player.UpdateBonePosition(skeleton.Joints, JointType.AnkleRight, JointType.FootRight);

							player.UpdateBonePosition(skeleton.Joints, JointType.HipLeft, JointType.HipCenter);
							player.UpdateBonePosition(skeleton.Joints, JointType.HipCenter, JointType.HipRight);

							// 胴体
							player.UpdateBonePosition(skeleton.Joints, JointType.HipCenter, JointType.ShoulderCenter);
						}
						playerId++;
					}
				}
			}

			using (ColorImageFrame colorImageFrame = eventArgs.OpenColorImageFrame()) {
				if (colorImageFrame != null) {
					colorImageFrame.CopyPixelDataTo(this.mPixelBuffer);
					this.mMainImageBuffer.Lock();
					this.mMainImageBuffer.WritePixels(
						new Int32Rect(0, 0, colorImageFrame.Width, colorImageFrame.Height),
						this.mPixelBuffer, colorImageFrame.Width * 4, 0);
					this.mMainImageBuffer.Unlock();
				}
			}
		}

		private void UninitializeKinect(KinectSensor kinectSensor)
		{
			kinectSensor.Stop();
			kinectSensor.Dispose();
			this.mCurrentKinectSensor = null;
		}

		private void CheckPlayers()
		{
			// プレイヤーの削除
			foreach (var player in this.mPlayers) {
				if (player.Value.IsAlive == false) {
					this.mGameState |= GameState.GameOver;
					this.mGameOverStartTime = DateTime.Now;
					this.mLastGameOverPlayer = player.Value;
					this.mHighScores.Add(new KeyValuePair<int, TimeSpan>(this.mLastGameOverPlayer.Score,
						DateTime.Now.Subtract(this.mFallingItemsManager.GameStartTime)));
					this.mPlayers.Remove(player.Value.Id);
					break;
				}
			}

			// ゲームモードの設定
			// 有効なプレイヤー数をカウント
			int playersCount = this.mPlayers.Count(player => player.Value.IsAlive);

			if (playersCount != this.mAlivePlayersCount) {
				switch (playersCount) {
					case 2:
						this.mGameState |= GameState.Main;
						this.mGameState &= ~GameState.Title;
						this.mFallingItemsManager.SetGameMode(GameMode.TwoPlayer);
						break;
					case 1:
						this.mGameState |= GameState.Main;
						this.mGameState &= ~GameState.Title;
						this.mFallingItemsManager.SetGameMode(GameMode.OnePlayer);
						break;
					case 0:
						this.mGameState &= ~GameState.Main;
						this.mGameState |= GameState.Title;
						this.mFallingItemsManager.SetGameMode(GameMode.NoPlayer);
						break;
				}
				this.mAlivePlayersCount = playersCount;
			}
		}

		private void OnPlayFieldSizeChanged(object sender, SizeChangedEventArgs eventArgs)
		{
			this.UpdatePlayFieldSize();
		}

		private void UpdatePlayFieldSize()
		{
			this.mScreenRect.X = 0.0;
			this.mScreenRect.Y = 0.0;
			this.mScreenRect.Width = this.mPlayFieldCanvas.ActualWidth;
			this.mScreenRect.Height = this.mPlayFieldCanvas.ActualHeight;

			this.mPlayerBounds.X = 0.0;
			this.mPlayerBounds.Y = this.mPlayFieldCanvas.ActualHeight * 0.2;
			this.mPlayerBounds.Width = this.mPlayFieldCanvas.ActualWidth;
			this.mPlayerBounds.Height = this.mPlayFieldCanvas.ActualHeight * 0.75;

			foreach (var player in this.mPlayers) {
				player.Value.SetPlayerBounds(this.mPlayerBounds);
			}

			this.mFallingItemBounds = this.mPlayerBounds;
			this.mFallingItemBounds.Y = 0.0;
			this.mFallingItemBounds.Height = this.mPlayFieldCanvas.ActualHeight;

			if (this.mFallingItemsManager != null) {
				this.mFallingItemsManager.SetSceneRect(this.mFallingItemBounds);
			}
		}

		private void GameThread()
		{
			this.mIsGameThreadRunning = true;
			this.mTimeNextFrame = DateTime.Now;
			this.mActualFrameTime = 1000.0 / this.mTargetFrameRate;

			while (this.mIsGameThreadRunning) {
				DateTime currentTime = DateTime.Now;

				if (this.mTimeLastFrame == DateTime.MinValue) {
					this.mTimeLastFrame = currentTime;
				}

				double deltaTime = currentTime.Subtract(this.mTimeLastFrame).TotalMilliseconds;

				// 1フレームの処理にかかった時間を計算
				this.mActualFrameTime = this.mActualFrameTime * 0.95 + deltaTime * 0.05;

				// 現在のフレームレートを計算
				this.mCurrentFrameRate = 1000.0 / this.mActualFrameTime;

				// 最後のフレームの処理終了時間を更新
				this.mTimeLastFrame = currentTime;

				// 処理フレーム数を更新
				this.mFrameCount++;

				// 処理が追いつかない場合はフレームレートを下げる
				if ((this.mFrameCount % 100 == 0) && (this.mCurrentFrameRate < this.mTargetFrameRate * 0.92)) {
					this.mTargetFrameRate = Math.Max(MainWindow.MinFrameRate, (this.mTargetFrameRate + this.mCurrentFrameRate) / 2.0);
				}

				if (currentTime > this.mTimeNextFrame) {
					// 処理が追いつかない場合
					this.mTimeNextFrame = currentTime;
				} else {
					// 処理が時間内に完了した場合はスリープ
					double waitMilliSeconds = this.mTimeNextFrame.Subtract(currentTime).TotalMilliseconds;
					if (waitMilliSeconds >= MainWindow.TimerResolution) {
						Thread.Sleep((int)(waitMilliSeconds + 0.5));
					}
				}

				// 次のフレームの処理終了時間を更新
				this.mTimeNextFrame += TimeSpan.FromMilliseconds(1000.0 / this.mTargetFrameRate);

				this.Dispatcher.Invoke(DispatcherPriority.Send, new Action<int>(this.HandleGameTimer), 0);
			}
		}

		private void HandleGameTimer(int param)
		{
			if (this.mFrameCount % 100 == 0) {
				this.mCurrentFrameRate = 1000.0 / this.mActualFrameTime;
				this.mFallingItemsManager.SetFrameRate(this.mCurrentFrameRate);
			}
			
			for (int i = 0; i < MainWindow.IntraFrameCount; i++) {
				foreach (var player in this.mPlayers) {
					HitType hitType = this.mFallingItemsManager.CheckHits(player.Value);

					switch (hitType) {
						case HitType.CoinGet:
							this.mSoundCoinGet.Play();
							break;
						case HitType.HitCherry:
							this.mSoundHit.Play();
							break;
						case HitType.HitBigCherry:
							this.mSoundHit.Play();
							break;
						case HitType.HitSpike:
							this.mSoundHit.Play();
							break;
						case HitType.OneUp:
							this.mSoundOneUp.Play();
							break;
					}
				}
				this.mFallingItemsManager.AdvanceFrame();
			}

			this.mPlayFieldCanvas.Children.Clear();
			this.mFallingItemsManager.Draw(this.mPlayFieldCanvas.Children, this.mPlayers);

			foreach (var player in this.mPlayers) {
				player.Value.Draw(this.mPlayFieldCanvas.Children);
			}
			
			this.CheckPlayers();

			if (this.mGameState.HasFlag(GameState.Title)) {
				this.DrawTitle();
			} else if (this.mGameState.HasFlag(GameState.GameOver)) {
				if (DateTime.Now.Subtract(this.mGameOverStartTime).TotalMilliseconds < 1500) {
					this.DrawGameOver();
					this.mFallingItemsManager.SetDropRate(0);
					this.mFallingItemsManager.SetGravity(0);
					this.mFallingItemsManager.DissolveAllItems();
				} else {
					this.mGameState &= ~GameState.GameOver;

					if (this.mAlivePlayersCount == 0) {
						this.mGameState &= GameState.Title;
					}

					this.mFallingItemsManager.SetDropRate(this.mDropRate);
					this.mFallingItemsManager.SetGravity(this.mGravity);
				}
			}
		}

		private void DrawTitle()
		{
			double opacity = (this.mFrameCount % 128.0) >= 64.0 ?
				(255.0 / 64.0) * (this.mFrameCount % 64.0) :
				255.0 - (255.0 / 64.0) * (this.mFrameCount % 64.0);
			
			Label label = FallingItemsManager.CreateLabel(
				"Kinect落ちものゲーム",
				new Rect(0.0, 0.0, 
					this.mPlayFieldCanvas.ActualWidth, this.mPlayFieldCanvas.ActualHeight * 0.5),
				new SolidColorBrush(Colors.Red));
			label.HorizontalContentAlignment = HorizontalAlignment.Center;
			label.VerticalContentAlignment = VerticalAlignment.Bottom;
			label.FontSize = Math.Max(1.0, 
				Math.Min(this.mPlayFieldCanvas.ActualWidth / 12.0, this.mPlayFieldCanvas.ActualHeight / 12.0));
			label.FontWeight = FontWeight.FromOpenTypeWeight(600);
			label.Opacity = opacity / 255.0;

			this.mPlayFieldCanvas.Children.Add(label);

			var sortedHighScores = this.mHighScores.OrderByDescending(highScore => highScore.Key);
			int score = 0;
			TimeSpan timeSpan;
			string labelText = string.Empty;

			if (sortedHighScores.Count() < 3) {
				for (int i = 0; i < sortedHighScores.Count(); i++) {
					score = sortedHighScores.ElementAt(i).Key;
					timeSpan = sortedHighScores.ElementAt(i).Value;
					labelText += $"{i + 1}位: {score}Pt ({timeSpan.Minutes}:{timeSpan.Seconds.ToString("00")})\n";
                }
			} else {
				for (int i = 0; i < 3; i++) {
					score = sortedHighScores.ElementAt(i).Key;
					timeSpan = sortedHighScores.ElementAt(i).Value;
					labelText += $"{i + 1}位: {score}Pt ({timeSpan.Minutes}:{timeSpan.Seconds.ToString("00")})\n";
				}
			}
			
			Label scoreLabel = FallingItemsManager.CreateLabel(
				labelText,
				new Rect(0.0, this.mPlayFieldCanvas.ActualHeight * 0.5,
					this.mPlayFieldCanvas.ActualWidth, this.mPlayFieldCanvas.ActualHeight * 0.5),
				new SolidColorBrush(Colors.Red));
			scoreLabel.HorizontalContentAlignment = HorizontalAlignment.Center;
			scoreLabel.VerticalContentAlignment = VerticalAlignment.Top;
			scoreLabel.FontSize = Math.Max(1.0,
				Math.Min(this.mPlayFieldCanvas.ActualWidth / 24.0, this.mPlayFieldCanvas.ActualHeight / 24.0));
			scoreLabel.FontWeight = FontWeight.FromOpenTypeWeight(500);
			scoreLabel.Opacity = 1.0;

			this.mPlayFieldCanvas.Children.Add(scoreLabel);
		}

		private void DrawGameOver()
		{
			string labelText = string.Empty;

			switch (this.mLastGameOverPlayer.State) {
				case PlayerState.Disappeared:
					labelText = $"Player{this.mLastGameOverPlayer.Id}: Disappeared!";
					break;
				case PlayerState.GameOver:
					labelText = $"Player{this.mLastGameOverPlayer.Id}: Game Over!";
					break;
            }

			Label label = FallingItemsManager.CreateLabel(
				labelText,
				new Rect(0.0, 0.0, this.mPlayFieldCanvas.ActualWidth, this.mPlayFieldCanvas.ActualHeight),
				new SolidColorBrush(Colors.Red));
			label.HorizontalContentAlignment = HorizontalAlignment.Center;
			label.VerticalContentAlignment = VerticalAlignment.Center;
			label.FontSize = Math.Max(1.0,
				Math.Min(this.mPlayFieldCanvas.ActualWidth / 12.0, this.mPlayFieldCanvas.ActualHeight / 12.0));
			label.FontWeight = FontWeight.FromOpenTypeWeight(600);
			label.Opacity = 1.0;
			
			this.mPlayFieldCanvas.Children.Add(label);
		}

		private void OnBGMEnded(object sender, EventArgs eventArgs)
		{
			MediaPlayer mediaPlayer = sender as MediaPlayer;

			if (mediaPlayer == null) {
				return;
			}

			this.mBGMPlayer.Position = new TimeSpan(0);
			this.mBGMPlayer.Play();
		}
		
	}
}
