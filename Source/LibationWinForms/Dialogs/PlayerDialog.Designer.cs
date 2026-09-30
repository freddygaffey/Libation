namespace LibationWinForms.Dialogs
{
	partial class PlayerDialog
	{
		private System.ComponentModel.IContainer components = null;

		protected override void Dispose(bool disposing)
		{
			if (disposing && (components != null))
			{
				components.Dispose();
			}
			base.Dispose(disposing);
		}

		#region Windows Form Designer generated code

		private void InitializeComponent()
		{
			this.layoutPanel = new System.Windows.Forms.TableLayoutPanel();
			this.titleLbl = new System.Windows.Forms.Label();
			this.authorsLbl = new System.Windows.Forms.Label();
			this.chapterCb = new System.Windows.Forms.ComboBox();
			this.positionTbar = new System.Windows.Forms.TrackBar();
			this.timePanel = new System.Windows.Forms.TableLayoutPanel();
			this.elapsedLbl = new System.Windows.Forms.Label();
			this.remainingLbl = new System.Windows.Forms.Label();
			this.buttonsPanel = new System.Windows.Forms.FlowLayoutPanel();
			this.prevChapterBtn = new System.Windows.Forms.Button();
			this.skipBackBtn = new System.Windows.Forms.Button();
			this.playPauseBtn = new System.Windows.Forms.Button();
			this.skipForwardBtn = new System.Windows.Forms.Button();
			this.nextChapterBtn = new System.Windows.Forms.Button();
			this.speedPanel = new System.Windows.Forms.TableLayoutPanel();
			this.speedLbl = new System.Windows.Forms.Label();
			this.speedTbar = new System.Windows.Forms.TrackBar();
			this.speedValueLbl = new System.Windows.Forms.Label();
			this.speedPresetsPanel = new System.Windows.Forms.FlowLayoutPanel();
			this.volumePanel = new System.Windows.Forms.TableLayoutPanel();
			this.volumeLbl = new System.Windows.Forms.Label();
			this.volumeTbar = new System.Windows.Forms.TrackBar();
			this.toolTip = new System.Windows.Forms.ToolTip();
			this.layoutPanel.SuspendLayout();
			((System.ComponentModel.ISupportInitialize)(this.positionTbar)).BeginInit();
			this.timePanel.SuspendLayout();
			this.buttonsPanel.SuspendLayout();
			this.speedPanel.SuspendLayout();
			((System.ComponentModel.ISupportInitialize)(this.speedTbar)).BeginInit();
			this.volumePanel.SuspendLayout();
			((System.ComponentModel.ISupportInitialize)(this.volumeTbar)).BeginInit();
			this.SuspendLayout();
			//
			// layoutPanel
			//
			this.layoutPanel.ColumnCount = 1;
			this.layoutPanel.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
			this.layoutPanel.Controls.Add(this.titleLbl, 0, 0);
			this.layoutPanel.Controls.Add(this.authorsLbl, 0, 1);
			this.layoutPanel.Controls.Add(this.chapterCb, 0, 2);
			this.layoutPanel.Controls.Add(this.positionTbar, 0, 3);
			this.layoutPanel.Controls.Add(this.timePanel, 0, 4);
			this.layoutPanel.Controls.Add(this.buttonsPanel, 0, 5);
			this.layoutPanel.Controls.Add(this.speedPanel, 0, 6);
			this.layoutPanel.Controls.Add(this.speedPresetsPanel, 0, 7);
			this.layoutPanel.Controls.Add(this.volumePanel, 0, 8);
			this.layoutPanel.Dock = System.Windows.Forms.DockStyle.Fill;
			this.layoutPanel.Location = new System.Drawing.Point(0, 0);
			this.layoutPanel.Name = "layoutPanel";
			this.layoutPanel.Padding = new System.Windows.Forms.Padding(9);
			this.layoutPanel.RowCount = 9;
			for (int i = 0; i < this.layoutPanel.RowCount; i++)
				this.layoutPanel.RowStyles.Add(new System.Windows.Forms.RowStyle());
			this.layoutPanel.Size = new System.Drawing.Size(484, 361);
			this.layoutPanel.TabIndex = 0;
			//
			// titleLbl
			//
			this.titleLbl.AutoEllipsis = true;
			this.titleLbl.Dock = System.Windows.Forms.DockStyle.Fill;
			this.titleLbl.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Bold);
			this.titleLbl.Name = "titleLbl";
			this.titleLbl.Size = new System.Drawing.Size(460, 19);
			this.titleLbl.TabIndex = 0;
			//
			// authorsLbl
			//
			this.authorsLbl.AutoEllipsis = true;
			this.authorsLbl.Dock = System.Windows.Forms.DockStyle.Fill;
			this.authorsLbl.Name = "authorsLbl";
			this.authorsLbl.Size = new System.Drawing.Size(460, 19);
			this.authorsLbl.TabIndex = 1;
			//
			// chapterCb
			//
			this.chapterCb.Dock = System.Windows.Forms.DockStyle.Fill;
			this.chapterCb.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
			this.chapterCb.FormattingEnabled = true;
			this.chapterCb.Name = "chapterCb";
			this.chapterCb.TabIndex = 2;
			this.chapterCb.SelectionChangeCommitted += new System.EventHandler(this.chapterCb_SelectionChangeCommitted);
			//
			// positionTbar
			//
			this.positionTbar.Dock = System.Windows.Forms.DockStyle.Fill;
			this.positionTbar.Name = "positionTbar";
			this.positionTbar.TabIndex = 3;
			this.positionTbar.TickStyle = System.Windows.Forms.TickStyle.None;
			this.positionTbar.Scroll += new System.EventHandler(this.positionTbar_Scroll);
			//
			// timePanel
			//
			this.timePanel.AutoSize = true;
			this.timePanel.ColumnCount = 2;
			this.timePanel.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 50F));
			this.timePanel.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 50F));
			this.timePanel.Controls.Add(this.elapsedLbl, 0, 0);
			this.timePanel.Controls.Add(this.remainingLbl, 1, 0);
			this.timePanel.Dock = System.Windows.Forms.DockStyle.Fill;
			this.timePanel.Name = "timePanel";
			this.timePanel.RowCount = 1;
			this.timePanel.RowStyles.Add(new System.Windows.Forms.RowStyle());
			this.timePanel.TabIndex = 4;
			//
			// elapsedLbl
			//
			this.elapsedLbl.AutoSize = true;
			this.elapsedLbl.Anchor = System.Windows.Forms.AnchorStyles.Left;
			this.elapsedLbl.Name = "elapsedLbl";
			this.elapsedLbl.TabIndex = 0;
			//
			// remainingLbl
			//
			this.remainingLbl.AutoSize = true;
			this.remainingLbl.Anchor = System.Windows.Forms.AnchorStyles.Right;
			this.remainingLbl.Name = "remainingLbl";
			this.remainingLbl.TabIndex = 1;
			this.toolTip.SetToolTip(this.remainingLbl, "Time remaining at the current speed");
			//
			// buttonsPanel
			//
			this.buttonsPanel.Anchor = System.Windows.Forms.AnchorStyles.None;
			this.buttonsPanel.AutoSize = true;
			this.buttonsPanel.Controls.Add(this.prevChapterBtn);
			this.buttonsPanel.Controls.Add(this.skipBackBtn);
			this.buttonsPanel.Controls.Add(this.playPauseBtn);
			this.buttonsPanel.Controls.Add(this.skipForwardBtn);
			this.buttonsPanel.Controls.Add(this.nextChapterBtn);
			this.buttonsPanel.Name = "buttonsPanel";
			this.buttonsPanel.TabIndex = 5;
			this.buttonsPanel.WrapContents = false;
			//
			// prevChapterBtn
			//
			this.prevChapterBtn.AutoSize = true;
			this.prevChapterBtn.Name = "prevChapterBtn";
			this.prevChapterBtn.TabIndex = 0;
			this.prevChapterBtn.Text = "Prev";
			this.toolTip.SetToolTip(this.prevChapterBtn, "Previous chapter");
			this.prevChapterBtn.Click += new System.EventHandler(this.prevChapterBtn_Click);
			//
			// skipBackBtn
			//
			this.skipBackBtn.AutoSize = true;
			this.skipBackBtn.Name = "skipBackBtn";
			this.skipBackBtn.TabIndex = 1;
			this.skipBackBtn.Text = "-30s";
			this.toolTip.SetToolTip(this.skipBackBtn, "Back 30 seconds (Left arrow)");
			this.skipBackBtn.Click += new System.EventHandler(this.skipBackBtn_Click);
			//
			// playPauseBtn
			//
			this.playPauseBtn.MinimumSize = new System.Drawing.Size(80, 0);
			this.playPauseBtn.AutoSize = true;
			this.playPauseBtn.Name = "playPauseBtn";
			this.playPauseBtn.TabIndex = 2;
			this.playPauseBtn.Text = "Play";
			this.toolTip.SetToolTip(this.playPauseBtn, "Play or pause (Space)");
			this.playPauseBtn.Click += new System.EventHandler(this.playPauseBtn_Click);
			//
			// skipForwardBtn
			//
			this.skipForwardBtn.AutoSize = true;
			this.skipForwardBtn.Name = "skipForwardBtn";
			this.skipForwardBtn.TabIndex = 3;
			this.skipForwardBtn.Text = "+30s";
			this.toolTip.SetToolTip(this.skipForwardBtn, "Forward 30 seconds (Right arrow)");
			this.skipForwardBtn.Click += new System.EventHandler(this.skipForwardBtn_Click);
			//
			// nextChapterBtn
			//
			this.nextChapterBtn.AutoSize = true;
			this.nextChapterBtn.Name = "nextChapterBtn";
			this.nextChapterBtn.TabIndex = 4;
			this.nextChapterBtn.Text = "Next";
			this.toolTip.SetToolTip(this.nextChapterBtn, "Next chapter");
			this.nextChapterBtn.Click += new System.EventHandler(this.nextChapterBtn_Click);
			//
			// speedPanel
			//
			this.speedPanel.AutoSize = true;
			this.speedPanel.ColumnCount = 3;
			this.speedPanel.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
			this.speedPanel.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
			this.speedPanel.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 48F));
			this.speedPanel.Controls.Add(this.speedLbl, 0, 0);
			this.speedPanel.Controls.Add(this.speedTbar, 1, 0);
			this.speedPanel.Controls.Add(this.speedValueLbl, 2, 0);
			this.speedPanel.Dock = System.Windows.Forms.DockStyle.Fill;
			this.speedPanel.Name = "speedPanel";
			this.speedPanel.RowCount = 1;
			this.speedPanel.RowStyles.Add(new System.Windows.Forms.RowStyle());
			this.speedPanel.TabIndex = 6;
			//
			// speedLbl
			//
			this.speedLbl.Anchor = System.Windows.Forms.AnchorStyles.Left;
			this.speedLbl.AutoSize = true;
			this.speedLbl.Name = "speedLbl";
			this.speedLbl.TabIndex = 0;
			this.speedLbl.Text = "Speed";
			//
			// speedTbar
			//
			this.speedTbar.Dock = System.Windows.Forms.DockStyle.Fill;
			this.speedTbar.LargeChange = 5;
			this.speedTbar.Name = "speedTbar";
			this.speedTbar.TabIndex = 1;
			this.speedTbar.TickFrequency = 5;
			this.speedTbar.Scroll += new System.EventHandler(this.speedTbar_Scroll);
			//
			// speedValueLbl
			//
			this.speedValueLbl.Anchor = System.Windows.Forms.AnchorStyles.Right;
			this.speedValueLbl.AutoSize = true;
			this.speedValueLbl.Name = "speedValueLbl";
			this.speedValueLbl.TabIndex = 2;
			//
			// speedPresetsPanel
			//
			this.speedPresetsPanel.Anchor = System.Windows.Forms.AnchorStyles.None;
			this.speedPresetsPanel.AutoSize = true;
			this.speedPresetsPanel.Name = "speedPresetsPanel";
			this.speedPresetsPanel.TabIndex = 7;
			//
			// volumePanel
			//
			this.volumePanel.AutoSize = true;
			this.volumePanel.ColumnCount = 2;
			this.volumePanel.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
			this.volumePanel.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
			this.volumePanel.Controls.Add(this.volumeLbl, 0, 0);
			this.volumePanel.Controls.Add(this.volumeTbar, 1, 0);
			this.volumePanel.Dock = System.Windows.Forms.DockStyle.Fill;
			this.volumePanel.Name = "volumePanel";
			this.volumePanel.RowCount = 1;
			this.volumePanel.RowStyles.Add(new System.Windows.Forms.RowStyle());
			this.volumePanel.TabIndex = 8;
			//
			// volumeLbl
			//
			this.volumeLbl.Anchor = System.Windows.Forms.AnchorStyles.Left;
			this.volumeLbl.AutoSize = true;
			this.volumeLbl.Name = "volumeLbl";
			this.volumeLbl.TabIndex = 0;
			this.volumeLbl.Text = "Volume";
			//
			// volumeTbar
			//
			this.volumeTbar.Dock = System.Windows.Forms.DockStyle.Fill;
			this.volumeTbar.LargeChange = 10;
			this.volumeTbar.Maximum = 100;
			this.volumeTbar.Name = "volumeTbar";
			this.volumeTbar.TabIndex = 1;
			this.volumeTbar.TickStyle = System.Windows.Forms.TickStyle.None;
			this.volumeTbar.Scroll += new System.EventHandler(this.volumeTbar_Scroll);
			//
			// PlayerDialog
			//
			this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.ClientSize = new System.Drawing.Size(484, 361);
			this.Controls.Add(this.layoutPanel);
			this.MinimumSize = new System.Drawing.Size(420, 400);
			this.Name = "PlayerDialog";
			this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
			this.layoutPanel.ResumeLayout(false);
			this.layoutPanel.PerformLayout();
			((System.ComponentModel.ISupportInitialize)(this.positionTbar)).EndInit();
			this.timePanel.ResumeLayout(false);
			this.timePanel.PerformLayout();
			this.buttonsPanel.ResumeLayout(false);
			this.buttonsPanel.PerformLayout();
			this.speedPanel.ResumeLayout(false);
			this.speedPanel.PerformLayout();
			((System.ComponentModel.ISupportInitialize)(this.speedTbar)).EndInit();
			this.volumePanel.ResumeLayout(false);
			this.volumePanel.PerformLayout();
			((System.ComponentModel.ISupportInitialize)(this.volumeTbar)).EndInit();
			this.ResumeLayout(false);

		}

		#endregion

		private System.Windows.Forms.TableLayoutPanel layoutPanel;
		private System.Windows.Forms.Label titleLbl;
		private System.Windows.Forms.Label authorsLbl;
		private System.Windows.Forms.ComboBox chapterCb;
		private System.Windows.Forms.TrackBar positionTbar;
		private System.Windows.Forms.TableLayoutPanel timePanel;
		private System.Windows.Forms.Label elapsedLbl;
		private System.Windows.Forms.Label remainingLbl;
		private System.Windows.Forms.FlowLayoutPanel buttonsPanel;
		private System.Windows.Forms.Button prevChapterBtn;
		private System.Windows.Forms.Button skipBackBtn;
		private System.Windows.Forms.Button playPauseBtn;
		private System.Windows.Forms.Button skipForwardBtn;
		private System.Windows.Forms.Button nextChapterBtn;
		private System.Windows.Forms.TableLayoutPanel speedPanel;
		private System.Windows.Forms.Label speedLbl;
		private System.Windows.Forms.TrackBar speedTbar;
		private System.Windows.Forms.Label speedValueLbl;
		private System.Windows.Forms.FlowLayoutPanel speedPresetsPanel;
		private System.Windows.Forms.TableLayoutPanel volumePanel;
		private System.Windows.Forms.Label volumeLbl;
		private System.Windows.Forms.TrackBar volumeTbar;
		private System.Windows.Forms.ToolTip toolTip;
	}
}
