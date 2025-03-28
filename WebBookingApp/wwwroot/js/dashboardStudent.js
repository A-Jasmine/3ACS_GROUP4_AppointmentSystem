// Function to open the upload modal
function openUploadModal() {
    document.getElementById("uploadModal").style.display = "flex";
}

// Function to close the upload modal
function closeUploadModal() {
    document.getElementById("uploadModal").style.display = "none";
}

// Function to show the logout modal
function showLogoutModal() {
    document.getElementById("logoutModal").style.display = "flex";
}

// Function to hide the logout modal
function hideLogoutModal() {
    document.getElementById("logoutModal").style.display = "none";
}

// Function to confirm logout
function confirmLogout() {
    document.getElementById("logoutForm").submit();
}

// ✅ Upload Image & Update Profile Picture Instantly
function uploadImage() {
    const fileInput = document.getElementById("fileInput");
    const uploadMessage = document.getElementById("uploadMessage");
    const profilePic = document.getElementById("profilePic"); // Sidebar profile picture

    if (!fileInput.files.length) {
        uploadMessage.textContent = "Please select a file to upload.";
        uploadMessage.style.color = "red";
        return;
    }

    const formData = new FormData();
    formData.append("UploadedFile", fileInput.files[0]);

    fetch("/dashboardStudent?handler=UploadPicture", {
        method: "POST",
        body: formData
    })
        .then(response => response.json())
        .then(data => {
            if (data.success) {
                uploadMessage.textContent = "Upload successful!";
                uploadMessage.style.color = "green";

                // ✅ Update profile picture instantly (cache-busting trick)
                profilePic.src = "/dashboardStudent?handler=ProfilePicture&t=" + new Date().getTime();

                // ✅ Close modal after upload
                setTimeout(closeUploadModal, 1000);
            } else {
                uploadMessage.textContent = "Upload failed. " + data.error;
                uploadMessage.style.color = "red";
            }
        })
        .catch(error => {
            uploadMessage.textContent = "Upload failed. Please try again.";
            uploadMessage.style.color = "red";
        });
}

// ✅ Close modals when clicking outside
window.onclick = function (event) {
    const uploadModal = document.getElementById("uploadModal");
    const logoutModal = document.getElementById("logoutModal");

    if (event.target === uploadModal) {
        closeUploadModal();
    }
    if (event.target === logoutModal) {
        hideLogoutModal();
    }


    // Handle booking form submission
    document.addEventListener('DOMContentLoaded', function () {
        const bookingForm = document.getElementById('bookingForm');
        if (bookingForm) {
            bookingForm.addEventListener('submit', function (e) {
                e.preventDefault();
                const profId = document.getElementById('professorId').value;
                const date = document.getElementById('appointmentDate').value;

                // TODO: Add your fetch POST request to submit the booking
                alert("Booked Professor ID: " + profId + " on " + date);

                // Close the modal after booking
                bootstrap.Modal.getInstance(document.getElementById('bookingModal')).hide();
            });
        }
    });

};
