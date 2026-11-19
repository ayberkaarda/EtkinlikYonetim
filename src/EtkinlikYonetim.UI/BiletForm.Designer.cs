namespace EtkinlikYonetim.UI
{
    partial class BiletForm
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.lblEtkinlik = new System.Windows.Forms.Label();
            this.picQr = new System.Windows.Forms.PictureBox();
            this.lblKod = new System.Windows.Forms.Label();
            this.txtKod = new System.Windows.Forms.TextBox();
            this.lblBilgi = new System.Windows.Forms.Label();
            ((System.ComponentModel.ISupportInitialize)(this.picQr)).BeginInit();
            this.SuspendLayout();
            // 
            // lblEtkinlik
            // 
            this.lblEtkinlik.Location = new System.Drawing.Point(12, 12);
            this.lblEtkinlik.Name = "lblEtkinlik";
            this.lblEtkinlik.Size = new System.Drawing.Size(376, 40);
            this.lblEtkinlik.TabIndex = 0;
            this.lblEtkinlik.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // picQr
            // 
            this.picQr.BackColor = System.Drawing.Color.White;
            this.picQr.Location = new System.Drawing.Point(50, 60);
            this.picQr.Name = "picQr";
            this.picQr.Size = new System.Drawing.Size(300, 300);
            this.picQr.SizeMode = System.Windows.Forms.PictureBoxSizeMode.Zoom;
            this.picQr.TabIndex = 1;
            this.picQr.TabStop = false;
            // 
            // lblKod
            // 
            this.lblKod.AutoSize = true;
            this.lblKod.Location = new System.Drawing.Point(12, 374);
            this.lblKod.Name = "lblKod";
            this.lblKod.Size = new System.Drawing.Size(70, 16);
            this.lblKod.TabIndex = 2;
            this.lblKod.Text = "Bilet kodu";
            // 
            // txtKod
            // 
            this.txtKod.Location = new System.Drawing.Point(12, 394);
            this.txtKod.Name = "txtKod";
            this.txtKod.ReadOnly = true;
            this.txtKod.Size = new System.Drawing.Size(376, 22);
            this.txtKod.TabIndex = 3;
            // 
            // lblBilgi
            // 
            this.lblBilgi.Location = new System.Drawing.Point(12, 424);
            this.lblBilgi.Name = "lblBilgi";
            this.lblBilgi.Size = new System.Drawing.Size(376, 40);
            this.lblBilgi.TabIndex = 4;
            this.lblBilgi.Text = "Girişte bu QR kodu okutun. Bilet kodunu kimseyle paylaşmayın; kodu bilen bileti kullanabilir.";
            // 
            // BiletForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(8F, 16F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(400, 476);
            this.Controls.Add(this.lblBilgi);
            this.Controls.Add(this.txtKod);
            this.Controls.Add(this.lblKod);
            this.Controls.Add(this.picQr);
            this.Controls.Add(this.lblEtkinlik);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "BiletForm";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "Bilet";
            ((System.ComponentModel.ISupportInitialize)(this.picQr)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Label lblEtkinlik;
        private System.Windows.Forms.PictureBox picQr;
        private System.Windows.Forms.Label lblKod;
        private System.Windows.Forms.TextBox txtKod;
        private System.Windows.Forms.Label lblBilgi;
    }
}