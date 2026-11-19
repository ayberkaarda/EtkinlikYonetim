namespace EtkinlikYonetim.UI
{
    partial class KullaniciYonetForm
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
            this.lblHesaplar = new System.Windows.Forms.Label();
            this.lstHesaplar = new System.Windows.Forms.ListBox();
            this.btnEtkinlestir = new System.Windows.Forms.Button();
            this.btnDevreDisi = new System.Windows.Forms.Button();
            this.btnYoneticiYap = new System.Windows.Forms.Button();
            this.btnKullaniciYap = new System.Windows.Forms.Button();
            this.lblAciklama = new System.Windows.Forms.Label();
            this.SuspendLayout();
            //
            // lblHesaplar
            //
            this.lblHesaplar.AutoSize = true;
            this.lblHesaplar.Location = new System.Drawing.Point(12, 12);
            this.lblHesaplar.Name = "lblHesaplar";
            this.lblHesaplar.Size = new System.Drawing.Size(62, 16);
            this.lblHesaplar.TabIndex = 0;
            this.lblHesaplar.Text = "Hesaplar";
            //
            // lstHesaplar
            //
            this.lstHesaplar.FormattingEnabled = true;
            this.lstHesaplar.IntegralHeight = false;
            this.lstHesaplar.ItemHeight = 16;
            this.lstHesaplar.Location = new System.Drawing.Point(12, 34);
            this.lstHesaplar.Name = "lstHesaplar";
            this.lstHesaplar.Size = new System.Drawing.Size(360, 300);
            this.lstHesaplar.TabIndex = 1;
            //
            // btnEtkinlestir
            //
            this.btnEtkinlestir.Location = new System.Drawing.Point(388, 34);
            this.btnEtkinlestir.Name = "btnEtkinlestir";
            this.btnEtkinlestir.Size = new System.Drawing.Size(170, 34);
            this.btnEtkinlestir.TabIndex = 2;
            this.btnEtkinlestir.Text = "Etkinleştir";
            this.btnEtkinlestir.UseVisualStyleBackColor = true;
            this.btnEtkinlestir.Click += new System.EventHandler(this.btnEtkinlestir_Click);
            //
            // btnDevreDisi
            //
            this.btnDevreDisi.Location = new System.Drawing.Point(388, 76);
            this.btnDevreDisi.Name = "btnDevreDisi";
            this.btnDevreDisi.Size = new System.Drawing.Size(170, 34);
            this.btnDevreDisi.TabIndex = 3;
            this.btnDevreDisi.Text = "Devre Dışı Bırak";
            this.btnDevreDisi.UseVisualStyleBackColor = true;
            this.btnDevreDisi.Click += new System.EventHandler(this.btnDevreDisi_Click);
            //
            // btnYoneticiYap
            //
            this.btnYoneticiYap.Location = new System.Drawing.Point(388, 132);
            this.btnYoneticiYap.Name = "btnYoneticiYap";
            this.btnYoneticiYap.Size = new System.Drawing.Size(170, 34);
            this.btnYoneticiYap.TabIndex = 4;
            this.btnYoneticiYap.Text = "Yönetici Yap";
            this.btnYoneticiYap.UseVisualStyleBackColor = true;
            this.btnYoneticiYap.Click += new System.EventHandler(this.btnYoneticiYap_Click);
            //
            // btnKullaniciYap
            //
            this.btnKullaniciYap.Location = new System.Drawing.Point(388, 174);
            this.btnKullaniciYap.Name = "btnKullaniciYap";
            this.btnKullaniciYap.Size = new System.Drawing.Size(170, 34);
            this.btnKullaniciYap.TabIndex = 5;
            this.btnKullaniciYap.Text = "Kullanıcı Yap";
            this.btnKullaniciYap.UseVisualStyleBackColor = true;
            this.btnKullaniciYap.Click += new System.EventHandler(this.btnKullaniciYap_Click);
            //
            // lblAciklama
            //
            this.lblAciklama.Location = new System.Drawing.Point(388, 228);
            this.lblAciklama.Name = "lblAciklama";
            this.lblAciklama.Size = new System.Drawing.Size(170, 106);
            this.lblAciklama.TabIndex = 6;
            this.lblAciklama.Text = "Kendi hesabınızı değiştiremezsiniz. Son etkin yönetici devre dışı bırakılamaz.";
            //
            // KullaniciYonetForm
            //
            this.AutoScaleDimensions = new System.Drawing.SizeF(8F, 16F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(572, 348);
            this.Controls.Add(this.lblAciklama);
            this.Controls.Add(this.btnKullaniciYap);
            this.Controls.Add(this.btnYoneticiYap);
            this.Controls.Add(this.btnDevreDisi);
            this.Controls.Add(this.btnEtkinlestir);
            this.Controls.Add(this.lstHesaplar);
            this.Controls.Add(this.lblHesaplar);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "KullaniciYonetForm";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "Kullanıcılar";
            this.Load += new System.EventHandler(this.KullaniciYonetForm_Load);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Label lblHesaplar;
        private System.Windows.Forms.ListBox lstHesaplar;
        private System.Windows.Forms.Button btnEtkinlestir;
        private System.Windows.Forms.Button btnDevreDisi;
        private System.Windows.Forms.Button btnYoneticiYap;
        private System.Windows.Forms.Button btnKullaniciYap;
        private System.Windows.Forms.Label lblAciklama;
    }
}
