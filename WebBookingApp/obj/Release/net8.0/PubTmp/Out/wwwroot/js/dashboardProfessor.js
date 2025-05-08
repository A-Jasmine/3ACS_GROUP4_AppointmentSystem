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
    const fileInput = document.querySelector("input[name='UploadedFile']");
    const uploadMessage = document.getElementById("uploadMessage");
    const profilePic = document.getElementById("profileImage"); // Sidebar profile picture

    if (!fileInput.files.length) {
        uploadMessage.textContent = "Please select a file to upload.";
        uploadMessage.style.color = "red";
        return;
    }

    const formData = new FormData();
    formData.append("UploadedFile", fileInput.files[0]);

    fetch(window.location.pathname + "?handler=UploadPicture", {
        method: "POST",
        body: formData
    })
        .then(response => {
            if (response.ok) return response.text();
            throw new Error("Upload failed");
        })
        .then(() => {
            uploadMessage.textContent = "Upload successful!";
            uploadMessage.style.color = "green";

            // ✅ Update profile picture instantly (cache-busting trick)
            profilePic.src = "/dashboardProfessor?handler=ProfilePicture&t=" + new Date().getTime();

            // ✅ Close modal after 1 second
            setTimeout(closeUploadModal, 1000);
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
};

// ✅ Blocked Dates Logic
const blockedDates = []; // Store blocked dates

function addBlockedDate() {
    const blockDateInput = document.getElementById('blockDate');
    const blockDateValue = blockDateInput.value;
    const blockedList = document.getElementById('blockedDatesList');

    if (blockDateValue && !blockedDates.includes(blockDateValue)) {
        blockedDates.push(blockDateValue);

        const listItem = document.createElement('li');
        listItem.className = 'list-group-item d-flex justify-content-between align-items-center';
        listItem.textContent = blockDateValue;

        const removeBtn = document.createElement('button');
        removeBtn.className = 'btn btn-sm btn-outline-danger';
        removeBtn.textContent = 'Remove';
        removeBtn.onclick = function () {
            blockedDates.splice(blockedDates.indexOf(blockDateValue), 1);
            listItem.remove();
        };

        listItem.appendChild(removeBtn);
        blockedList.appendChild(listItem);

        blockDateInput.value = '';
    } else {
        alert('Please select a valid date or avoid duplicates.');
    }
}

// ✅ Set the minimum date to today for availability and block date inputs
document.addEventListener('DOMContentLoaded', function () {
    const today = new Date().toISOString().split('T')[0];
    const availableDate = document.getElementById('availableDate');
    const blockDate = document.getElementById('blockDate');

    if (availableDate) availableDate.setAttribute('min', today);
    if (blockDate) blockDate.setAttribute('min', today);
});
